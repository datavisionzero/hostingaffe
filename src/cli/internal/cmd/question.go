package cmd

import (
	"context"
	"fmt"
	"net/http"

	"github.com/google/uuid"
	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

func newQuestion(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "question", Short: "Questions across the project: what waits for a human, and the answers."}
	cmd.AddCommand(newQuestionList(g), newQuestionAnswer(g))
	return cmd
}

func newQuestionList(g *globals) *cobra.Command {
	var (
		answered bool
		all      bool
		issue    string
		cursor   string
		limit    int
		search   string
	)
	cmd := &cobra.Command{
		Use:   "list",
		Short: "Open questions across the project, oldest first — what only a human can resolve.",
		Args:  cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			params := &api.ListQuestionsParams{Project: optional(cfg.Project), Issue: optional(issue), Q: optional(search), Cursor: optional(cursor)}
			open := true
			switch {
			case all:
				// both
			case answered:
				open = false
				params.Open = &open
			default:
				params.Open = &open
			}
			if cmd.Flags().Changed("limit") {
				v := int32(limit)
				params.Limit = &v
			}
			resp, err := c.ListQuestionsWithResponse(cmd.Context(), params, func(_ context.Context, req *http.Request) error {
				if all {
					q := req.URL.Query()
					q.Del("open")
					req.URL.RawQuery = q.Encode()
				}
				return nil
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
			render.Questions(cmd.OutOrStdout(), resp.JSON200.Items)
			if resp.JSON200.HasMore && resp.JSON200.NextCursor != nil {
				fmt.Fprintf(cmd.ErrOrStderr(), "%d of %d; next page: --cursor %s\n", len(resp.JSON200.Items), resp.JSON200.Total, *resp.JSON200.NextCursor)
			}
			return nil
		},
	}
	cmd.Flags().BoolVar(&answered, "answered", false, "the answered ones instead")
	cmd.Flags().BoolVar(&all, "all", false, "open and answered alike")
	cmd.Flags().StringVar(&issue, "issue", "", "only this issue's")
	cmd.Flags().StringVarP(&search, "query", "q", "", "full-text search in questions and answers")
	cmd.Flags().StringVar(&cursor, "cursor", "", "the next page, as the previous one said")
	cmd.Flags().IntVar(&limit, "limit", 50, "1 to 200")
	return cmd
}

func newQuestionAnswer(g *globals) *cobra.Command {
	var file string
	cmd := &cobra.Command{
		Use:   "answer ID [ANSWER]",
		Short: "Answer an open question; the issue becomes workable again.",
		Args:  cobra.RangeArgs(1, 2),
		RunE: func(cmd *cobra.Command, args []string) error {
			id, err := uuid.Parse(args[0])
			if err != nil {
				return &config.UsageError{Message: fmt.Sprintf("%q is not a question id; `ha question list` prints them.", args[0])}
			}
			text, err := textArg(cmd, args, file, "an answer")
			if err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.AnswerQuestionWithResponse(cmd.Context(), id, api.AnswerRequest{Answer: &text})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return renderJSON(cmd, resp.JSON200)
			}
			fmt.Fprintf(cmd.OutOrStdout(), "answered: %s\n", resp.JSON200.Question)
			return nil
		},
	}
	cmd.Flags().StringVar(&file, "file", "", "the answer as Markdown, from a file or `-` for stdin")
	return cmd
}

func renderJSON(cmd *cobra.Command, v any) error { return render.JSON(cmd.OutOrStdout(), v) }
