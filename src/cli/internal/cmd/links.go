package cmd

import (
	"context"
	"fmt"
	"regexp"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// A Markdown body names another thing of the record with a scheme and an
// address — `[Restoring a backup](page:backup-restore)` (ADR 0007). Nothing
// validates a body, because the instance stores Markdown and does not parse it,
// so a reference that points at nothing is written and kept like any other
// text. This is what says which ones do.
var schemes = map[string]string{"machine:": "machine", "software:": "software", "installation:": "installation", "page:": "page"}

var (
	// The two spellings of a link: inline, and the reference definition an
	// image or a repeated link uses. A destination in angle brackets is left
	// alone, the way the import leaves it alone.
	inlineLink     = regexp.MustCompile(`\[([^\]]*)\]\(([^\s()<>]+)(?:[ \t]+(?:"[^"]*"|'[^']*'))?\)`)
	definitionLink = regexp.MustCompile(`^[ ]{0,3}\[([^\]]+)\]:[ \t]*([^\s]+)`)
	// A fence opening or closing a code block, which nothing inside is a link.
	fenceLine = regexp.MustCompile("^[ ]{0,3}(`{3,}|~{3,})")
	// The shape of a key and of a page's slug, which are the same.
	addressShape = regexp.MustCompile(`^[a-z0-9]+(-[a-z0-9]+)*$`)
)

// reference is one link of the record found in one body.
type reference struct {
	Page    string `json:"page"`
	Text    string `json:"text"`
	Target  string `json:"target"`
	kind    string
	address string
}

// referencesIn finds every link of the record in a Markdown body. It is the
// same reading the import does when it rewrites a migrated repository's paths:
// line by line, and never inside a fence, where a link is the text of an
// example and not a link.
func referencesIn(page, body string) []reference {
	found := []reference{}
	fence := ""

	for _, line := range strings.Split(body, "\n") {
		if opened := fenceLine.FindStringSubmatch(line); opened != nil {
			switch {
			case fence == "":
				fence = opened[1]
			case fence[0] == opened[1][0] && len(opened[1]) >= len(fence):
				fence = ""
			}
			continue
		}
		if fence != "" {
			continue
		}

		for _, match := range inlineLink.FindAllStringSubmatch(line, -1) {
			found = append(found, oneReference(page, match[1], match[2])...)
		}
		if match := definitionLink.FindStringSubmatch(line); match != nil {
			found = append(found, oneReference(page, match[1], match[2])...)
		}
	}

	return found
}

// oneReference is the link if its target is one of the record's, and nothing
// otherwise: a foreign URL, a relative path, or a scheme carrying something
// that is not shaped like an address and therefore names nothing that could
// ever exist.
func oneReference(page, text, target string) []reference {
	for prefix, kind := range schemes {
		if !strings.HasPrefix(target, prefix) {
			continue
		}

		address := strings.TrimPrefix(target, prefix)
		if hash := strings.Index(address, "#"); hash >= 0 {
			address = address[:hash]
		}
		if !addressShape.MatchString(address) {
			continue
		}

		return []reference{{Page: page, Text: text, Target: target, kind: kind, address: address}}
	}

	return nil
}

// newPageCheck reads the bodies and says which of their references point at
// nothing. It is composed from the ordinary endpoints, the way `ha export` is:
// whoever may read the pages may make the reads this is built from.
//
// It is a report and not a gate. It exits 0 whether or not it found anything,
// because every exit code ha gives is derived from how the instance answered
// (docs/api.md, Exit codes) and a number ha invented from what it read would be
// the first that is not. `--json` is what a script branches on.
func newPageCheck(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "check [SLUG]", Short: "The references in the pages, and the ones that point at nothing.", Args: cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}

			slugs, err := pageSlugs(cmd.Context(), c, args)
			if err != nil {
				return err
			}

			found := []reference{}
			for _, slug := range slugs {
				page, err := c.ReadPageWithResponse(cmd.Context(), slug)
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(page.HTTPResponse, page.Body); err != nil {
					return err
				}
				found = append(found, referencesIn(page.JSON200.Slug, page.JSON200.Body)...)
			}

			dead, err := pointingAtNothing(cmd.Context(), c, found)
			if err != nil {
				return err
			}

			if g.json {
				return render.JSON(cmd.OutOrStdout(), dead)
			}
			for _, one := range dead {
				fmt.Fprintf(cmd.OutOrStdout(), "%-24s %-32s %s\n", one.Page, one.Target, one.Text)
			}
			return nil
		},
	}
}

// pageSlugs is the one page that was named, or every page there is.
func pageSlugs(ctx context.Context, c *client.Client, args []string) ([]string, error) {
	if len(args) == 1 {
		return args, nil
	}

	pages, err := c.ListPagesWithResponse(ctx, &api.ListPagesParams{})
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(pages.HTTPResponse, pages.Body); err != nil {
		return nil, err
	}

	slugs := make([]string, 0, len(*pages.JSON200))
	for _, page := range *pages.JSON200 {
		slugs = append(slugs, page.Slug)
	}
	return slugs, nil
}

// pointingAtNothing keeps the references whose address no live record has. The
// lists are asked for once each, and only for the kinds the bodies actually
// named: a wiki that links no software costs no call about software.
func pointingAtNothing(ctx context.Context, c *client.Client, found []reference) ([]reference, error) {
	live := map[string]map[string]bool{}

	dead := []reference{}
	for _, one := range found {
		if _, asked := live[one.kind]; !asked {
			keys, err := everyKeyOf(ctx, c, one.kind)
			if err != nil {
				return nil, err
			}
			live[one.kind] = keys
		}
		if !live[one.kind][one.address] {
			dead = append(dead, one)
		}
	}

	return dead, nil
}

// everyKeyOf is every address of one kind, retired ones included: a link to a
// retired machine leads to a machine that is still there and still readable,
// and calling it dead would send whoever reads the report looking for a
// mistake nobody made.
func everyKeyOf(ctx context.Context, c *client.Client, kind string) (map[string]bool, error) {
	keys := map[string]bool{}
	retired := true

	switch kind {
	case "machine":
		resp, err := c.ListMachinesWithResponse(ctx, &api.ListMachinesParams{Retired: &retired})
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return nil, err
		}
		for _, one := range *resp.JSON200 {
			keys[one.Key] = true
		}
	case "software":
		resp, err := c.ListSoftwareWithResponse(ctx)
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return nil, err
		}
		for _, one := range *resp.JSON200 {
			keys[one.Key] = true
		}
	case "installation":
		resp, err := c.ListInstallationsWithResponse(ctx, &api.ListInstallationsParams{Retired: &retired})
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return nil, err
		}
		for _, one := range *resp.JSON200 {
			keys[one.Key] = true
		}
	default:
		resp, err := c.ListPagesWithResponse(ctx, &api.ListPagesParams{})
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return nil, err
		}
		for _, one := range *resp.JSON200 {
			keys[one.Slug] = true
		}
	}

	return keys, nil
}
