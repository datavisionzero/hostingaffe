package cmd

import (
	"bytes"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// The page carries the same verbs as the machine, the software and the
// installation. It is not `put`: a file has a path and a content and nothing
// else, so putting it is the whole of what can be done with it, while a page
// has a title, Markdown, a kind and what it hangs on, and a write touches one
// of them. There is no alias on the old names.
func newPage(g *globals) *cobra.Command {
	cmd := &cobra.Command{
		Use:     "page",
		Short:   "Pages: the instance's flat wiki — Markdown addressed by a slug, for what is knowledge rather than an assignment.",
		Aliases: []string{"pages"},
	}
	cmd.AddCommand(
		newPageList(g), newPageView(g), newPageAdd(g), newPageSet(g),
		newPageRename(g), newPageDelete(g), newPageRestore(g), newPageHistory(g),
		newPageCheck(g))
	return cmd
}

func printPage(g *globals, cmd *cobra.Command, page api.Page) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), page)
	}
	render.Page(cmd.OutOrStdout(), page)
	return nil
}

func newPageList(g *globals) *cobra.Command {
	var query, kind, machine, installation string
	cmd := &cobra.Command{
		Use: "list", Short: "Every page, by slug, without the bodies.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			if machine != "" && installation != "" {
				return &config.UsageError{Message: "a page hangs on one thing: --machine or --installation, not both."}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListPagesWithResponse(cmd.Context(), &api.ListPagesParams{
				Q:            optional(query),
				Kind:         optional(kind),
				Machine:      optional(machine),
				Installation: optional(installation),
			})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.PageSummaries(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
	cmd.Flags().StringVarP(&query, "query", "q", "", "full text in the title and the body")
	cmd.Flags().StringVar(&kind, "kind", "", "only pages of this kind: runbook, decision or note")
	cmd.Flags().StringVar(&machine, "machine", "", "only pages hanging on this machine, by `key`")
	cmd.Flags().StringVar(&installation, "installation", "", "only pages hanging on this installation, by `key`")
	return cmd
}

// newPageView prints the head and then the Markdown as it is stored, so that
// the output can be piped straight back into `--body-file -`.
func newPageView(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "view SLUG", Short: "The page: the head, then the Markdown as it is stored.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadPageWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printPage(g, cmd, *resp.JSON200)
		},
	}
}

// pageFields are the flags a page is written with, on `add` and on `set` alike,
// so that what a page can be created with is what it can be corrected with.
type pageFields struct {
	title, bodyFile, kind, machine, installation string
	detach                                       bool
}

func (p *pageFields) flags(cmd *cobra.Command) {
	f := cmd.Flags()
	f.StringVar(&p.title, "title", "", "the one line that says what the page is")
	f.StringVar(&p.bodyFile, "body-file", "", "the Markdown, from a file or `-` for stdin")
	f.StringVar(&p.kind, "kind", "", "how the page is to be read: runbook, decision or note")
	f.StringVar(&p.machine, "machine", "", "hang the page on this machine, by `key`")
	f.StringVar(&p.installation, "installation", "", "hang the page on this installation, by `key`")
}

// anchorOf turns the two flags into the one anchor the contract carries. It is
// a usage error rather than a refusal from the instance, because `attached_to`
// holds one anchor and there is no request that says two: the mistake is in the
// arguments and never reaches the wire.
func (p *pageFields) anchorOf() (*api.Anchor, error) {
	switch {
	case p.machine != "" && p.installation != "":
		return nil, &config.UsageError{Message: "a page hangs on one thing: --machine or --installation, not both."}
	case p.machine != "":
		return &api.Anchor{Kind: api.AnchorKindMachine, Key: p.machine}, nil
	case p.installation != "":
		return &api.Anchor{Kind: api.AnchorKindInstallation, Key: p.installation}, nil
	default:
		return nil, nil
	}
}

func newPageAdd(g *globals) *cobra.Command {
	var write pageFields
	var note string
	cmd := &cobra.Command{
		Use: "add SLUG --title TITLE", Short: "Add a page; the slug is the address you give it, never derived from the title.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if write.title == "" {
				return &config.UsageError{Message: "a page has a title: --title."}
			}
			anchor, err := write.anchorOf()
			if err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			body, err := readText(cmd.InOrStdin(), write.bodyFile)
			if err != nil {
				return err
			}
			request := api.CreatePageRequest{Slug: &args[0], Title: &write.title, Body: body, AttachedTo: anchor}
			if write.kind != "" {
				of := api.PageKind(write.kind)
				request.Kind = &of
			}
			resp, err := c.CreatePageWithResponse(
				cmd.Context(), &api.CreatePageParams{Note: optional(note)}, request)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printPage(g, cmd, *resp.JSON201)
		},
	}
	write.flags(cmd)
	noteFlag(cmd, &note)
	return cmd
}

func newPageSet(g *globals) *cobra.Command {
	var write pageFields
	var note, ifMatch string
	cmd := &cobra.Command{
		Use: "set SLUG", Short: "Change the title, the Markdown, the kind or what the page hangs on; --if-match guards the document.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			anchor, err := write.anchorOf()
			if err != nil {
				return err
			}
			if write.detach && anchor != nil {
				return &config.UsageError{Message: "--detach gives the page to the instance; it does not go with --machine or --installation."}
			}
			changes := map[string]any{}
			if write.title != "" {
				changes["title"] = write.title
			}
			body, err := readText(cmd.InOrStdin(), write.bodyFile)
			if err != nil {
				return err
			}
			if body != nil {
				changes["body"] = *body
			}
			if write.kind != "" {
				changes["kind"] = write.kind
			}
			// Leaving the flags off lets the page hang where it hangs; only
			// --detach says out loud that it should hang on nothing, and that
			// is the null the contract wants.
			switch {
			case write.detach:
				changes["attached_to"] = nil
			case anchor != nil:
				changes["attached_to"] = anchor
			}
			if len(changes) == 0 {
				return &config.UsageError{Message: "nothing to change: --title, --body-file, --kind, --machine, --installation or --detach. The slug is `ha page rename`."}
			}
			return changePage(g, cmd, args[0], changes, ifMatch, note)
		},
	}
	write.flags(cmd)
	cmd.Flags().BoolVar(&write.detach, "detach", false, "hang the page on nothing: it belongs to the instance as a whole")
	noteFlag(cmd, &note)
	ifMatchFlag(cmd, &ifMatch)
	return cmd
}

// newPageRename is its own verb rather than a flag on `set`, because moving a
// page's address is not the same kind of act as editing its text: nothing
// forwards, and every link written to the old slug stops working (ADR 0021).
func newPageRename(g *globals) *cobra.Command {
	var note, ifMatch string
	cmd := &cobra.Command{
		Use: "rename SLUG NEW-SLUG", Short: "Move the page to a new address. Nothing forwards; the old slug leads nowhere.", Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			return changePage(g, cmd, args[0], map[string]any{"slug": args[1]}, ifMatch, note)
		},
	}
	noteFlag(cmd, &note)
	ifMatchFlag(cmd, &ifMatch)
	return cmd
}

func changePage(g *globals, cmd *cobra.Command, slug string, changes map[string]any, ifMatch, note string) error {
	_, c, err := g.load()
	if err != nil {
		return err
	}
	body := given(cmd)
	for field, value := range changes {
		body.set(field, value)
	}
	resp, err := c.ChangePageWithBodyWithResponse(
		cmd.Context(), slug, &api.ChangePageParams{Note: optional(note)},
		"application/json", bytes.NewReader(body.json()), guard(ifMatch))
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	return printPage(g, cmd, *resp.JSON200)
}

func newPageDelete(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{
		Use: "delete SLUG", Short: "Soft-delete a page; its slug stays taken until the grace period is over.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.DeletePageWithResponse(cmd.Context(), args[0], &api.DeletePageParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), map[string]any{"slug": args[0], "deleted": true})
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s deleted; `ha page restore %s` brings it back.\n", args[0], args[0])
			return nil
		},
	}
	noteFlag(cmd, &note)
	return cmd
}

func newPageRestore(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{
		Use: "restore SLUG", Short: "Bring a deleted page back, under the slug it kept.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RestorePageWithResponse(cmd.Context(), args[0], &api.RestorePageParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printPage(g, cmd, *resp.JSON200)
		},
	}
	noteFlag(cmd, &note)
	return cmd
}

func newPageHistory(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "history SLUG", Short: "Who changed what, oldest first, with the note that came with the change.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadPageHistoryWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.History(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
}
