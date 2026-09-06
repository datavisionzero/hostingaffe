package cmd

import (
	"context"
	"net/http"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// emptyResult is the one exit code that is not an API error: `next` found
// nothing, or a waiting command reached its deadline (docs/api.md).
type emptyResult struct{}

func (emptyResult) Error() string { return "nothing arrived before the deadline" }

const maximumServerWait = 3600

func newNext(g *globals) *cobra.Command {
	var (
		claim  bool
		ready  bool
		epic   string
		labels []string
		repo   string
		wait   int
	)

	cmd := &cobra.Command{
		Use:   "next",
		Short: "What the caller would be handed, in that order — or, with --claim, take the first and claim it in one act.",
		Long: `Without --claim, the "ready for agents" list of the project: every workable
issue for the caller, priority first, then an epic nobody else is working in,
then age (VISION 10). With --claim, the act at the centre of the product: the
highest-ranked workable issue is handed out and claimed in one transaction that
cannot be split. Exit 8 when nothing is workable, with the reasons.`,
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			waiting := cmd.Flags().Changed("wait")
			if waiting && !claim {
				return &config.UsageError{Message: "--wait requires --claim"}
			}
			if waiting && wait <= 0 {
				return &config.UsageError{Message: "--wait must be a positive number of seconds"}
			}

			round := wait
			if round > maximumServerWait {
				round = maximumServerWait
			}
			cfg, c, err := g.loadForWait(round)
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}

			// The repo label comes from the .hostingaffe file unless told
			// otherwise; `--repo none` asks for every issue of the project.
			repoLabel := cfg.Repo
			if cmd.Flags().Changed("repo") {
				repoLabel = repo
			}
			if repoLabel == "none" {
				repoLabel = ""
			}

			var (
				readyPtr *bool
				epicPtr  *string
				repoPtr  *string
				labelPtr *[]string
			)
			if ready {
				readyPtr = &ready
			}
			if epic != "" {
				epicPtr = &epic
			}
			if repoLabel != "" {
				repoPtr = &repoLabel
			}
			if len(labels) > 0 {
				labelPtr = &labels
			}

			ctx := cmd.Context()
			out := cmd.OutOrStdout()

			if !claim {
				// The generated params carry no `label`; the query is built by
				// the editor so that `label` repeats the way the API reads it.
				resp, err := c.PreviewNextWithResponse(ctx, project, &api.PreviewNextParams{Ready: readyPtr, Epic: epicPtr, Repo: repoPtr},
					withLabels(labels))
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
				if g.json {
					return render.JSON(out, resp.JSON200)
				}
				render.Summaries(out, resp.JSON200.Items)
				if len(resp.JSON200.Items) == 0 {
					render.Reasons(out, resp.JSON200.Reasons)
					return emptyResult{}
				}
				return nil
			}

			remaining := wait
			for {
				var waitPtr *int32
				seconds := remaining
				if waiting {
					if seconds > maximumServerWait {
						seconds = maximumServerWait
					}
					value := int32(seconds)
					waitPtr = &value
				}

				resp, err := c.TakeNextWithResponse(ctx, project, api.NextRequest{Ready: readyPtr, Epic: epicPtr, Repo: repoPtr, Label: labelPtr, Wait: waitPtr})
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}

				if resp.JSON200.Issue != nil || !waiting || remaining <= seconds {
					if g.json {
						if err := render.JSON(out, resp.JSON200); err != nil {
							return err
						}
					} else if resp.JSON200.Issue != nil {
						render.Issue(out, *resp.JSON200.Issue)
					} else {
						render.Reasons(out, resp.JSON200.Reasons)
					}

					if resp.JSON200.Issue == nil {
						return emptyResult{}
					}
					return nil
				}

				remaining -= seconds
			}
		},
	}

	cmd.Flags().BoolVar(&claim, "claim", false, "take the highest-ranked workable issue and claim it")
	cmd.Flags().BoolVar(&ready, "ready", false, "only flagged issues, even where triage is not required")
	cmd.Flags().StringVar(&epic, "epic", "", "only this epic's issues, e.g. PLAN-E3")
	cmd.Flags().StringArrayVar(&labels, "label", nil, "only issues carrying this label; repeatable, all must match")
	cmd.Flags().StringVar(&repo, "repo", "", "the `repo` label of this repository, or `none`; defaults to the .hostingaffe file")
	cmd.Flags().IntVar(&wait, "wait", 0, "wait this many seconds for a workable issue; requires --claim")

	return cmd
}

// withLabels adds `label=` once per label to a GET, which the generated
// parameters cannot express as an array.
func withLabels(labels []string) api.RequestEditorFn {
	return func(_ context.Context, req *http.Request) error {
		if len(labels) == 0 {
			return nil
		}
		q := req.URL.Query()
		for _, l := range labels {
			q.Add("label", l)
		}
		req.URL.RawQuery = q.Encode()
		return nil
	}
}
