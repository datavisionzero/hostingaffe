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

func newEpic(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "epic", Short: "Epics: a theme several issues hang under, and the living document that is their shared context."}
	cmd.AddCommand(newEpicCreate(g), newEpicList(g), newEpicView(g), newEpicEdit(g), newEpicClose(g), newEpicSimple(g, "reopen", "Back to open."), newEpicDelete(g), newEpicSimple(g, "restore", "Bring a deleted epic back."))
	return cmd
}

func printEpic(g *globals, cmd *cobra.Command, epic api.Epic) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), epic)
	}
	render.Epic(cmd.OutOrStdout(), epic)
	return nil
}

func newEpicCreate(g *globals) *cobra.Command {
	var descriptionFile string
	var labels []string
	cmd := &cobra.Command{
		Use: "create TITLE", Short: "Create an epic; the description is the plan every issue under it is read with.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			description, err := readText(cmd.InOrStdin(), descriptionFile)
			if err != nil {
				return err
			}
			body := api.CreateEpicRequest{Project: &project, Title: &args[0], Description: description}
			if len(labels) > 0 {
				body.Labels = &labels
			}
			resp, err := c.CreateEpicWithResponse(cmd.Context(), body)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printEpic(g, cmd, *resp.JSON201)
		},
	}
	cmd.Flags().StringVar(&descriptionFile, "description-file", "", "the plan as Markdown, from a file or `-` for stdin")
	cmd.Flags().StringArrayVar(&labels, "label", nil, "a label; repeatable")
	return cmd
}

func newEpicList(g *globals) *cobra.Command {
	var status, cursor string
	var labels []string
	var limit int
	cmd := &cobra.Command{
		Use: "list", Short: "A page of epics with their progress, newest first; open ones by default.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			params := &api.ListEpicsParams{Project: optional(cfg.Project), Status: optional(status), Cursor: optional(cursor)}
			if cmd.Flags().Changed("limit") {
				v := int32(limit)
				params.Limit = &v
			}
			resp, err := c.ListEpicsWithResponse(cmd.Context(), params, repeated("label", labels))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.EpicSummaries(cmd.OutOrStdout(), resp.JSON200.Items)
			if resp.JSON200.HasMore && resp.JSON200.NextCursor != nil {
				fmt.Fprintf(cmd.ErrOrStderr(), "%d of %d; next page: --cursor %s\n", len(resp.JSON200.Items), resp.JSON200.Total, *resp.JSON200.NextCursor)
			}
			return nil
		},
	}
	cmd.Flags().StringVar(&status, "status", "", "open (default), closed or all")
	cmd.Flags().StringArrayVar(&labels, "label", nil, "only epics carrying this label; repeatable")
	cmd.Flags().StringVar(&cursor, "cursor", "", "the next page, as the previous one said")
	cmd.Flags().IntVar(&limit, "limit", 50, "1 to 200")
	return cmd
}

func newEpicView(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "view KEY", Short: "The complete epic: the living document, the author, the labels, the progress.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadEpicWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printEpic(g, cmd, *resp.JSON200)
		},
	}
}

func newEpicEdit(g *globals) *cobra.Command {
	var title, descriptionFile, ifMatch string
	var labels []string
	cmd := &cobra.Command{
		Use: "edit KEY", Short: "Change the title, the living document or the labels; --if-match guards the document.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			changes := map[string]any{}
			if title != "" {
				changes["title"] = title
			}
			description, err := readText(cmd.InOrStdin(), descriptionFile)
			if err != nil {
				return err
			}
			if description != nil {
				changes["description"] = *description
			}
			if cmd.Flags().Changed("label") {
				changes["labels"] = labels
			}
			if len(changes) == 0 {
				return &config.UsageError{Message: "nothing to change: --title, --description-file or --label."}
			}
			body, _ := json.Marshal(changes)
			resp, err := c.ChangeEpicWithBodyWithResponse(cmd.Context(), args[0], "application/json", bytes.NewReader(body), func(_ context.Context, req *http.Request) error {
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
			return printEpic(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().StringVar(&title, "title", "", "the new title")
	cmd.Flags().StringVar(&descriptionFile, "description-file", "", "the plan as Markdown, from a file or `-` for stdin")
	cmd.Flags().StringArrayVar(&labels, "label", nil, "the whole label set; repeatable, replaces what is there")
	cmd.Flags().StringVar(&ifMatch, "if-match", "", "the updated_at as last read; refused as stale when it moved")
	return cmd
}

// newEpicClose closes the bracket, whatever is still open — and, because the
// vision has the CLI offer to cancel or park what is left in the same command
// (VISION 7), does so on a flag. Never a prompt: an agent must never hang.
func newEpicClose(g *globals) *cobra.Command {
	var cancelOpen, parkOpen bool
	cmd := &cobra.Command{
		Use: "close KEY [--cancel-open | --park-open]", Short: "Close the epic; it gates nothing. What is still open is listed, and canceled or parked on request.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if cancelOpen && parkOpen {
				return &config.UsageError{Message: "one of --cancel-open or --park-open."}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			key := strings.ToUpper(args[0])

			open, err := openIssuesOf(cmd, c, key)
			if err != nil {
				return err
			}

			resp, err := c.CloseEpicWithResponse(cmd.Context(), key)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}

			for _, issue := range open {
				switch {
				case cancelOpen:
					status := api.IssueStatus("canceled")
					reason := "Canceled with the epic " + key + "."
					r, err := c.CloseIssueWithResponse(cmd.Context(), issue.Key, api.CloseRequest{Status: &status, Result: &reason})
					if err != nil {
						return client.Transport(err)
					}
					if err := client.Check(r.HTTPResponse, r.Body); err != nil {
						return err
					}
				case parkOpen:
					r, err := c.ChangeIssueWithBodyWithResponse(cmd.Context(), issue.Key, "application/json", strings.NewReader(`{"status":"backlog"}`))
					if err != nil {
						return client.Transport(err)
					}
					if err := client.Check(r.HTTPResponse, r.Body); err != nil {
						// A claimed issue cannot be parked; say so and go on.
						fmt.Fprintf(cmd.ErrOrStderr(), "ha: %s not parked: %v\n", issue.Key, err)
					}
				}
			}

			if len(open) > 0 && !cancelOpen && !parkOpen {
				fmt.Fprintf(cmd.ErrOrStderr(), "ha: %s closed with %d issue(s) still open — they stay workable:\n", key, len(open))
				render.Summaries(cmd.ErrOrStderr(), open)
				fmt.Fprintln(cmd.ErrOrStderr(), "ha: `--cancel-open` cancels them, `--park-open` parks them, in the same command.")
			}
			return printEpic(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().BoolVar(&cancelOpen, "cancel-open", false, "cancel every issue still open under the epic")
	cmd.Flags().BoolVar(&parkOpen, "park-open", false, "park every issue still open under the epic")
	return cmd
}

// openIssuesOf lists what is still open under the epic: everything but done
// and canceled, in one page of the maximum size.
func openIssuesOf(cmd *cobra.Command, c *client.Client, epic string) ([]api.IssueSummary, error) {
	limit := int32(200)
	resp, err := c.ListIssuesWithResponse(cmd.Context(), &api.ListIssuesParams{Epic: &epic, Limit: &limit},
		repeated("status", []string{"backlog", "todo", "in_progress", "review"}))
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	return resp.JSON200.Items, nil
}

func newEpicSimple(g *globals, verb, short string) *cobra.Command {
	return &cobra.Command{
		Use: verb + " KEY", Short: short, Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			var epic *api.Epic
			switch verb {
			case "reopen":
				resp, err := c.ReopenEpicWithResponse(cmd.Context(), args[0])
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
				epic = resp.JSON200
			case "restore":
				resp, err := c.RestoreEpicWithResponse(cmd.Context(), args[0])
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
				epic = resp.JSON200
			}
			return printEpic(g, cmd, *epic)
		},
	}
}

func newEpicDelete(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "delete KEY", Short: "Soft-delete an epic nothing references; refused with the count while issues do.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.DeleteEpicWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			key := strings.ToUpper(args[0])
			if g.json {
				return render.JSON(cmd.OutOrStdout(), map[string]any{"key": key, "deleted": true})
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s deleted; `ha epic restore %s` brings it back.\n", key, key)
			return nil
		},
	}
}
