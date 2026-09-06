package cmd

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

func newPage(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "page", Short: "Pages: the project's flat wiki — Markdown addressed by a slug, for what is knowledge rather than an assignment."}
	cmd.AddCommand(newPageList(g), newPageView(g), newPageCreate(g), newPageEdit(g), newPageRename(g), newPageDelete(g), newPageRestore(g))
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
	var labels []string
	var query string
	cmd := &cobra.Command{
		Use: "list", Short: "Every page of the project, by slug, without the bodies.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			params := &api.ListPagesParams{Q: optional(query)}
			if len(labels) > 0 {
				params.Label = &labels
			}
			resp, err := c.ListPagesWithResponse(cmd.Context(), project, params)
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
	cmd.Flags().StringArrayVar(&labels, "label", nil, "only pages carrying this label; repeatable, all must match")
	cmd.Flags().StringVarP(&query, "query", "q", "", "full text in the title and the body")
	return cmd
}

// newPageView prints the head and then the Markdown as it is stored, so that
// the output can be piped straight back into `--body-file -`.
func newPageView(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "view SLUG", Short: "The page: the head, then the Markdown as it is stored.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			resp, err := c.ReadPageWithResponse(cmd.Context(), project, args[0])
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

func newPageCreate(g *globals) *cobra.Command {
	var title, bodyFile string
	var labels []string
	cmd := &cobra.Command{
		Use: "create SLUG --title TITLE", Short: "Create a page; the slug is the address you give it, never derived from the title.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			if title == "" {
				return &config.UsageError{Message: "a page has a title: --title."}
			}
			body, err := readText(cmd.InOrStdin(), bodyFile)
			if err != nil {
				return err
			}
			request := api.CreatePageRequest{Slug: &args[0], Title: &title, Body: body}
			if len(labels) > 0 {
				request.Labels = &labels
			}
			resp, err := c.CreatePageWithResponse(cmd.Context(), project, request)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printPage(g, cmd, *resp.JSON201)
		},
	}
	cmd.Flags().StringVar(&title, "title", "", "the one line that says what the page is")
	cmd.Flags().StringVar(&bodyFile, "body-file", "", "the Markdown, from a file or `-` for stdin")
	cmd.Flags().StringArrayVar(&labels, "label", nil, "a label; repeatable")
	return cmd
}

func newPageEdit(g *globals) *cobra.Command {
	var title, bodyFile, ifMatch string
	var labels []string
	cmd := &cobra.Command{
		Use: "edit SLUG", Short: "Change the title, the Markdown or the labels; --if-match guards the document.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			changes := map[string]any{}
			if title != "" {
				changes["title"] = title
			}
			body, err := readText(cmd.InOrStdin(), bodyFile)
			if err != nil {
				return err
			}
			if body != nil {
				changes["body"] = *body
			}
			if cmd.Flags().Changed("label") {
				changes["labels"] = labels
			}
			if len(changes) == 0 {
				return &config.UsageError{Message: "nothing to change: --title, --body-file or --label. The slug is `ha page rename`."}
			}
			return changePage(g, cmd, args[0], changes, ifMatch)
		},
	}
	cmd.Flags().StringVar(&title, "title", "", "the new title")
	cmd.Flags().StringVar(&bodyFile, "body-file", "", "the Markdown, from a file or `-` for stdin")
	cmd.Flags().StringArrayVar(&labels, "label", nil, "the whole label set; repeatable, replaces what is there")
	cmd.Flags().StringVar(&ifMatch, "if-match", "", "the updated_at as last read; refused as stale when it moved")
	return cmd
}

// newPageRename is its own verb rather than a flag on `edit`, because moving a
// page's address is not the same kind of act as editing its text: nothing
// forwards, and every link written to the old slug stops working (ADR 0021).
func newPageRename(g *globals) *cobra.Command {
	var ifMatch string
	cmd := &cobra.Command{
		Use: "rename SLUG NEW-SLUG", Short: "Move the page to a new address. Nothing forwards; the old slug leads nowhere.", Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			return changePage(g, cmd, args[0], map[string]any{"slug": args[1]}, ifMatch)
		},
	}
	cmd.Flags().StringVar(&ifMatch, "if-match", "", "the updated_at as last read; refused as stale when it moved")
	return cmd
}

func changePage(g *globals, cmd *cobra.Command, slug string, changes map[string]any, ifMatch string) error {
	cfg, c, err := g.load()
	if err != nil {
		return err
	}
	project, err := requireProject(cfg)
	if err != nil {
		return err
	}
	body, _ := json.Marshal(changes)
	resp, err := c.ChangePageWithBodyWithResponse(cmd.Context(), project, slug, "application/json", bytes.NewReader(body), func(_ context.Context, req *http.Request) error {
		if ifMatch != "" {
			req.Header.Set("If-Match", `"`+strings.Trim(ifMatch, `"`)+`"`)
		}
		return nil
	})
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	return printPage(g, cmd, *resp.JSON200)
}

func newPageDelete(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "delete SLUG", Short: "Soft-delete a page; its slug stays taken until the grace period is over.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			resp, err := c.DeletePageWithResponse(cmd.Context(), project, args[0])
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
}

func newPageRestore(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "restore SLUG", Short: "Bring a deleted page back, under the slug it kept.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			resp, err := c.RestorePageWithResponse(cmd.Context(), project, args[0])
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
