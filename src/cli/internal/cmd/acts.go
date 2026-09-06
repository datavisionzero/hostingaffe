package cmd

import (
	"fmt"
	"math"
	"strings"
	"time"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
)

// The acts on an issue (ADR 0016): each one endpoint with a rule inside it,
// each printing the complete issue it acted on. They hang under `ha issue`.
func issueActs(g *globals) []*cobra.Command {
	return []*cobra.Command{
		newIssueClaim(g), newIssueRelease(g), newIssueClose(g), newIssueReview(g), newIssueReopen(g),
		newIssuePark(g, "park", "backlog", "Park: the explicit decision that it is not up yet."),
		newIssuePark(g, "unpark", "todo", "Unpark: back to todo, due as soon as it is workable."),
		newIssueComment(g), newIssueAsk(g),
	}
}

func newIssueClaim(g *globals) *cobra.Command {
	var force bool
	cmd := &cobra.Command{
		Use:   "claim KEY",
		Short: "Claim: taken when unclaimed or expired, extended when yours, `claim-held` otherwise unless --force.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			body := api.ClaimRequest{}
			if force {
				body.Force = &force
			}
			resp, err := c.ClaimIssueWithResponse(cmd.Context(), args[0], body)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printIssue(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().BoolVar(&force, "force", false, "take over somebody else's claim; over a user's, only as a user")
	return cmd
}

func newIssueRelease(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "release KEY",
		Short: "Let go: the claim is cleared and the status is todo.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReleaseIssueWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printIssue(g, cmd, *resp.JSON200)
		},
	}
}

func newIssueClose(g *globals) *cobra.Command {
	var (
		done       bool
		canceled   bool
		resultFile string
	)
	cmd := &cobra.Command{
		Use:   "close KEY --done | --canceled [--result-file F]",
		Short: "Close. An agent's close lands in review where review is required; a user's where it says.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if done == canceled {
				return &config.UsageError{Message: "one of --done or --canceled."}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			result, err := readText(cmd.InOrStdin(), resultFile)
			if err != nil {
				return err
			}
			status := api.IssueStatus("done")
			if canceled {
				status = "canceled"
			}
			resp, err := c.CloseIssueWithResponse(cmd.Context(), args[0], api.CloseRequest{Status: &status, Result: result})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			// Expected, never enforced (VISION 8): pointed out, and that is all.
			if resp.JSON200.Result == nil || *resp.JSON200.Result == "" {
				fmt.Fprintf(cmd.ErrOrStderr(), "ha: %s closed without a result; `--result-file` says what was done, or why not.\n", resp.JSON200.Key)
			}
			if resp.JSON200.OpenSubIssues > 0 {
				fmt.Fprintf(cmd.ErrOrStderr(), "ha: %s closed with %d sub-issue(s) still open; they are no longer workable:\n", resp.JSON200.Key, resp.JSON200.OpenSubIssues)
				for _, child := range resp.JSON200.SubIssues {
					fmt.Fprintf(cmd.ErrOrStderr(), "  %s  %s\n", child.Key, child.Title)
				}
			}
			return printIssue(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().BoolVar(&done, "done", false, "the work the issue asked for is delivered")
	cmd.Flags().BoolVar(&canceled, "canceled", false, "it will not be done; the result says why")
	cmd.Flags().StringVar(&resultFile, "result-file", "", "what was done, as Markdown, from a file or `-` for stdin")
	return cmd
}

func newIssueReview(g *globals) *cobra.Command {
	var resultFile string
	cmd := &cobra.Command{
		Use:   "review KEY [--result-file F]",
		Short: "Hand in explicitly, whatever the switch says: the claim is released and a human looks.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			result, err := readText(cmd.InOrStdin(), resultFile)
			if err != nil {
				return err
			}
			resp, err := c.ReviewIssueWithResponse(cmd.Context(), args[0], api.ReviewRequest{Result: result})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if resp.JSON200.Result == nil || *resp.JSON200.Result == "" {
				fmt.Fprintf(cmd.ErrOrStderr(), "ha: %s handed in without a result; the reviewer reads `--result-file` first.\n", resp.JSON200.Key)
			}
			return printIssue(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().StringVar(&resultFile, "result-file", "", "what was done, as Markdown, from a file or `-` for stdin")
	return cmd
}

func newIssueReopen(g *globals) *cobra.Command {
	var comment, commentFile string
	cmd := &cobra.Command{
		Use:   "reopen KEY [--comment TEXT | --comment-file F]",
		Short: "Back to todo from review, done or canceled. Back from review is the rejection and wants a comment.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			text := optional(comment)
			if commentFile != "" {
				text, err = readText(cmd.InOrStdin(), commentFile)
				if err != nil {
					return err
				}
			}

			// The one act that wants to know where the issue came from: the
			// rejection out of review is expected to say why (VISION 9).
			fromReview := false
			if text == nil {
				before, err := c.ReadIssueWithResponse(cmd.Context(), args[0])
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(before.HTTPResponse, before.Body); err != nil {
					return err
				}
				fromReview = before.JSON200.Status == "review"
			}

			resp, err := c.ReopenIssueWithResponse(cmd.Context(), args[0], api.ReopenRequest{Comment: text})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if fromReview {
				fmt.Fprintf(cmd.ErrOrStderr(), "ha: %s sent back from review without a comment; the next agent reads `--comment` beside the old result.\n", resp.JSON200.Key)
			}
			return printIssue(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().StringVar(&comment, "comment", "", "what was not enough")
	cmd.Flags().StringVar(&commentFile, "comment-file", "", "the same, as Markdown from a file or `-`")
	return cmd
}

func newIssuePark(g *globals, use, status, short string) *cobra.Command {
	return &cobra.Command{
		Use:   use + " KEY",
		Short: short,
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			body := strings.NewReader(`{"status":"` + status + `"}`)
			resp, err := c.ChangeIssueWithBodyWithResponse(cmd.Context(), args[0], "application/json", body)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printIssue(g, cmd, *resp.JSON200)
		},
	}
}

func newIssueComment(g *globals) *cobra.Command {
	var file string
	cmd := &cobra.Command{
		Use:   "comment KEY [TEXT]",
		Short: "A note that forces nobody to act. Whoever can go on comments.",
		Args:  cobra.RangeArgs(1, 2),
		RunE: func(cmd *cobra.Command, args []string) error {
			text, err := textArg(cmd, args, file, "a comment")
			if err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.CommentOnIssueWithResponse(cmd.Context(), args[0], api.CommentRequest{Body: &text})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return renderJSON(cmd, resp.JSON201)
			}
			fmt.Fprintf(cmd.OutOrStdout(), "commented on %s as %s\n", strings.ToUpper(args[0]), resp.JSON201.Author.Name)
			return nil
		},
	}
	cmd.Flags().StringVar(&file, "file", "", "the comment as Markdown, from a file or `-` for stdin")
	return cmd
}

func newIssueAsk(g *globals) *cobra.Command {
	var (
		file string
		wait int
	)
	cmd := &cobra.Command{
		Use:   "ask KEY [QUESTION]",
		Short: "Ask: whoever cannot go on says on what. Does not release the claim.",
		Args:  cobra.RangeArgs(1, 2),
		RunE: func(cmd *cobra.Command, args []string) error {
			waiting := cmd.Flags().Changed("wait")
			if waiting && wait <= 0 {
				return &config.UsageError{Message: "--wait must be a positive number of seconds"}
			}
			text, err := textArg(cmd, args, file, "a question")
			if err != nil {
				return err
			}
			round := wait
			if round > maximumServerWait {
				round = maximumServerWait
			}
			_, c, err := g.loadForWait(round)
			if err != nil {
				return err
			}
			resp, err := c.AskQuestionWithResponse(cmd.Context(), args[0], api.AskRequest{Question: &text})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			question := resp.JSON201
			if waiting {
				remaining := wait
				issue, err := c.ReadIssueWithResponse(cmd.Context(), args[0])
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(issue.HTTPResponse, issue.Body); err != nil {
					return err
				}
				if issue.JSON200.Claim != nil && issue.JSON200.Claim.Holder.Id == question.AskedBy.Id && issue.JSON200.Claim.ExpiresAt != nil {
					claimSeconds := int(math.Ceil(time.Until(*issue.JSON200.Claim.ExpiresAt).Seconds()))
					if claimSeconds < remaining {
						remaining = claimSeconds
					}
				}

				for remaining > 0 && question.Answer == nil {
					seconds := remaining
					if seconds > maximumServerWait {
						seconds = maximumServerWait
					}
					value := int32(seconds)
					read, err := c.ReadQuestionWithResponse(cmd.Context(), question.Id, &api.ReadQuestionParams{Wait: &value})
					if err != nil {
						return client.Transport(err)
					}
					if err := client.Check(read.HTTPResponse, read.Body); err != nil {
						return err
					}
					question = read.JSON200
					remaining -= seconds
				}
			}
			if g.json {
				if err := renderJSON(cmd, question); err != nil {
					return err
				}
			} else if question.Answer != nil {
				fmt.Fprintf(cmd.OutOrStdout(), "answered on %s: %s\n", strings.ToUpper(args[0]), *question.Answer)
			} else {
				fmt.Fprintf(cmd.OutOrStdout(), "asked on %s: %s\nanswer with: ha question answer %s \"…\"\n", strings.ToUpper(args[0]), question.Question, question.Id)
			}
			if waiting && question.Answer == nil {
				return emptyResult{}
			}
			return nil
		},
	}
	cmd.Flags().StringVar(&file, "file", "", "the question as Markdown, from a file or `-` for stdin")
	cmd.Flags().IntVar(&wait, "wait", 0, "wait this many seconds for the answer, at most until the caller's claim expires")
	return cmd
}

// textArg is the text of a comment, a question or an answer: the second
// argument, or --file (a file or `-`), and one of the two.
func textArg(cmd *cobra.Command, args []string, file, what string) (string, error) {
	switch {
	case file != "" && len(args) > 1:
		return "", &config.UsageError{Message: fmt.Sprintf("either the text or --file for %s, not both.", what)}
	case file != "":
		text, err := readText(cmd.InOrStdin(), file)
		if err != nil {
			return "", err
		}
		return *text, nil
	case len(args) > 1:
		return args[1], nil
	default:
		return "", &config.UsageError{Message: fmt.Sprintf("%s needs a text: as the second argument, or with --file.", what)}
	}
}
