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

func newIssue(g *globals) *cobra.Command {
	cmd := &cobra.Command{
		Use:   "issue",
		Short: "Issues: create, list, view, edit, delete, restore, history, labels and blockers.",
	}
	cmd.AddCommand(
		newIssueCreate(g), newIssueList(g), newIssueView(g), newIssueEdit(g),
		newIssueDelete(g), newIssueRestore(g), newIssueHistory(g), newIssueLabel(g), newIssueBlock(g), newIssueUnblock(g))
	cmd.AddCommand(issueActs(g)...)
	return cmd
}

// ---------------------------------------------------------------- create ----

func newIssueCreate(g *globals) *cobra.Command {
	var (
		descriptionFile string
		priority        int
		ready           bool
		labels          []string
		epic            string
		parent          string
		assignee        string
		blockedBy       []string
		blocks          []string
		repo            string
		backlog         bool
		file            string
	)

	cmd := &cobra.Command{
		Use:   "create [TITLE]",
		Short: "Create an issue — or several wired-up ones from a file, in one transaction.",
		Long: `Creates one issue from the flags, or several from --file: the bulk body of
docs/api.md, refs and blockers included, all of them or none. The repo label of
the .hostingaffe file is added unless --repo none says otherwise.`,
		Args: cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}

			repoLabel := cfg.Repo
			if cmd.Flags().Changed("repo") {
				repoLabel = repo
			}
			if repoLabel == "none" {
				repoLabel = ""
			}

			var body api.CreateIssuesRequest
			switch {
			case file != "" && len(args) > 0:
				return &config.UsageError{Message: "either a title or --file, not both."}
			case file != "":
				text, err := readText(cmd.InOrStdin(), file)
				if err != nil {
					return err
				}
				if err := json.Unmarshal([]byte(*text), &body); err != nil {
					return &config.UsageError{Message: fmt.Sprintf("%s: not the bulk body of docs/api.md: %v", file, err)}
				}
			case len(args) == 0:
				return &config.UsageError{Message: "a title, or --file with several issues."}
			default:
				description, err := readText(cmd.InOrStdin(), descriptionFile)
				if err != nil {
					return err
				}
				item := api.NewIssue{Title: &args[0], Description: description, Epic: optional(epic), Parent: optional(parent), Assignee: optional(assignee)}
				if cmd.Flags().Changed("priority") {
					p := api.Priority(priority)
					item.Priority = &p
				}
				if ready {
					item.Ready = &ready
				}
				if len(labels) > 0 {
					item.Labels = &labels
				}
				if len(blockedBy) > 0 {
					item.BlockedBy = &blockedBy
				}
				if len(blocks) > 0 {
					item.Blocks = &blocks
				}
				if backlog {
					status := api.IssueStatus("backlog")
					item.Status = &status
				}
				body.Issues = &[]api.NewIssue{item}
			}

			if body.Project == nil || *body.Project == "" {
				project, err := requireProject(cfg)
				if err != nil {
					return err
				}
				body.Project = &project
			}

			if repoLabel != "" && body.Issues != nil {
				for i := range *body.Issues {
					item := &(*body.Issues)[i]
					if item.Labels == nil {
						item.Labels = &[]string{}
					}
					if !contains(*item.Labels, repoLabel) {
						*item.Labels = append(*item.Labels, repoLabel)
					}
				}
			}

			resp, err := c.CreateIssuesWithResponse(cmd.Context(), body)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}

			out := cmd.OutOrStdout()
			if g.json {
				if len(resp.JSON201.Items) == 1 && file == "" {
					return render.JSON(out, resp.JSON201.Items[0])
				}
				return render.JSON(out, resp.JSON201)
			}
			for i, issue := range resp.JSON201.Items {
				if i > 0 {
					fmt.Fprintln(out)
				}
				render.Issue(out, issue)
			}
			return nil
		},
	}

	cmd.Flags().StringVar(&descriptionFile, "description-file", "", "the description as Markdown, from a file or `-` for stdin")
	cmd.Flags().IntVar(&priority, "priority", 0, "0 none, 1 low, 2 medium, 3 high, 4 urgent")
	cmd.Flags().BoolVar(&ready, "ready", false, "concrete enough to implement without asking first")
	cmd.Flags().StringArrayVar(&labels, "label", nil, "a label; repeatable")
	cmd.Flags().StringVar(&epic, "epic", "", "the epic, e.g. PLAN-E3")
	cmd.Flags().StringVar(&parent, "parent", "", "the parent issue, e.g. PLAN-42")
	cmd.Flags().StringVar(&assignee, "assignee", "", "who it belongs to, by name")
	cmd.Flags().StringArrayVar(&blockedBy, "blocked-by", nil, "an issue this one waits for; repeatable")
	cmd.Flags().StringArrayVar(&blocks, "blocks", nil, "an issue that waits for this one; repeatable")
	cmd.Flags().StringVar(&repo, "repo", "", "the `repo` label to add, or `none`; defaults to the .hostingaffe file")
	cmd.Flags().BoolVar(&backlog, "backlog", false, "park it from birth")
	cmd.Flags().StringVar(&file, "file", "", "several issues at once: the bulk body of docs/api.md as JSON, from a file or `-`")

	return cmd
}

func contains(list []string, s string) bool {
	for _, l := range list {
		if l == s {
			return true
		}
	}
	return false
}

// ------------------------------------------------------------------ list ----

func newIssueList(g *globals) *cobra.Command {
	var (
		statuses        []string
		ready           bool
		priorityMin     int
		priorityMax     int
		labels          []string
		epic            string
		assignee        string
		claimed         string
		author          string
		blocked         bool
		hasOpenQuestion bool
		deleted         bool
		sort            string
		order           string
		cursor          string
		limit           int
		search          string
	)

	cmd := &cobra.Command{
		Use:   "list",
		Short: "A page of slim issues, filtered (ADR 0012).",
		Args:  cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}

			params := &api.ListIssuesParams{
				Project: optional(cfg.Project), Epic: optional(epic), Assignee: optional(assignee),
				Claimed: optional(claimed), Author: optional(author), Q: optional(search), Sort: optional(sort), Order: optional(order), Cursor: optional(cursor),
			}
			if ready {
				params.Ready = &ready
			}
			if cmd.Flags().Changed("priority-min") {
				v := int32(priorityMin)
				params.PriorityMin = &v
			}
			if cmd.Flags().Changed("priority-max") {
				v := int32(priorityMax)
				params.PriorityMax = &v
			}
			if blocked {
				params.Blocked = &blocked
			}
			if hasOpenQuestion {
				params.HasOpenQuestion = &hasOpenQuestion
			}
			if deleted {
				params.Deleted = &deleted
			}
			if cmd.Flags().Changed("limit") {
				v := int32(limit)
				params.Limit = &v
			}

			resp, err := c.ListIssuesWithResponse(cmd.Context(), params, repeated("status", statuses), repeated("label", labels))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}

			out := cmd.OutOrStdout()
			if g.json {
				return render.JSON(out, resp.JSON200)
			}
			render.Summaries(out, resp.JSON200.Items)
			if resp.JSON200.HasMore && resp.JSON200.NextCursor != nil {
				fmt.Fprintf(cmd.ErrOrStderr(), "%d of %d; next page: --cursor %s\n", len(resp.JSON200.Items), resp.JSON200.Total, *resp.JSON200.NextCursor)
			}
			return nil
		},
	}

	cmd.Flags().StringArrayVar(&statuses, "status", nil, "backlog, todo, in_progress, review, done, canceled; repeatable")
	cmd.Flags().BoolVar(&ready, "ready", false, "only flagged issues")
	cmd.Flags().IntVar(&priorityMin, "priority-min", 0, "at least this priority")
	cmd.Flags().IntVar(&priorityMax, "priority-max", 4, "at most this priority")
	cmd.Flags().StringArrayVar(&labels, "label", nil, "only issues carrying this label; repeatable, all must match")
	cmd.Flags().StringVar(&epic, "epic", "", "an epic key, or `none`")
	cmd.Flags().StringVar(&assignee, "assignee", "", "a name, `none` or `me`")
	cmd.Flags().StringVar(&claimed, "claimed", "", "`true`, `false`, a name or `me`")
	cmd.Flags().StringVar(&author, "author", "", "a name or `me`")
	cmd.Flags().BoolVar(&blocked, "blocked", false, "only issues with an open blocker")
	cmd.Flags().BoolVar(&hasOpenQuestion, "has-open-question", false, "only issues with an open question")
	cmd.Flags().BoolVar(&deleted, "deleted", false, "only issues in their grace period — the one read that sees deleted rows")
	cmd.Flags().StringVarP(&search, "query", "q", "", "full-text search in issue text, comments and questions")
	cmd.Flags().StringVar(&sort, "sort", "", "updated, created, priority or epic")
	cmd.Flags().StringVar(&order, "order", "", "asc or desc")
	cmd.Flags().StringVar(&cursor, "cursor", "", "the next page, as the previous one said")
	cmd.Flags().IntVar(&limit, "limit", 50, "1 to 200")

	return cmd
}

// repeated adds `name=` once per value to a GET, which the generated
// parameters cannot express as an array.
func repeated(name string, values []string) api.RequestEditorFn {
	return func(_ context.Context, req *http.Request) error {
		if len(values) == 0 {
			return nil
		}
		q := req.URL.Query()
		for _, v := range values {
			q.Add(name, v)
		}
		req.URL.RawQuery = q.Encode()
		return nil
	}
}

// ------------------------------------------------------------------ view ----

func newIssueView(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "view KEY",
		Short: "The complete issue: the context package, epic description and all.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadIssueWithResponse(cmd.Context(), args[0])
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

func printIssue(g *globals, cmd *cobra.Command, issue api.Issue) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), issue)
	}
	render.Issue(cmd.OutOrStdout(), issue)
	return nil
}

// ------------------------------------------------------------------ edit ----

func newIssueEdit(g *globals) *cobra.Command {
	var (
		title           string
		descriptionFile string
		resultFile      string
		priority        int
		ready           string
		assignee        string
		epic            string
		parent          string
		labels          []string
		status          string
		ifMatch         string
	)

	cmd := &cobra.Command{
		Use:   "edit KEY [KEY...]",
		Short: "Change the fields given on one or several issues; a bulk change is all or none.",
		Args:  cobra.RangeArgs(1, 100),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}

			// Only what was given goes on the wire: a PATCH field present as
			// null clears it, so the body is built by hand rather than from
			// the generated struct, whose unset pointers would all be null.
			changes := map[string]any{}
			if cmd.Flags().Changed("title") {
				changes["title"] = title
			}
			description, err := readText(cmd.InOrStdin(), descriptionFile)
			if err != nil {
				return err
			}
			if description != nil {
				changes["description"] = *description
			}
			result, err := readText(cmd.InOrStdin(), resultFile)
			if err != nil {
				return err
			}
			if result != nil {
				changes["result"] = *result
			}
			if cmd.Flags().Changed("priority") {
				changes["priority"] = priority
			}
			if ready != "" {
				switch ready {
				case "true":
					changes["ready"] = true
				case "false":
					changes["ready"] = false
				default:
					return &config.UsageError{Message: "--ready is true or false."}
				}
			}
			if given, value := noneOrValue(assignee); given {
				changes["assignee"] = value
			}
			if given, value := noneOrValue(epic); given {
				changes["epic"] = value
			}
			if given, value := noneOrValue(parent); given {
				changes["parent"] = value
			}
			if cmd.Flags().Changed("label") {
				changes["labels"] = labels
			}
			if status != "" {
				changes["status"] = status
			}
			if len(changes) == 0 {
				return &config.UsageError{Message: "nothing to change: give at least one field."}
			}
			if len(args) > 1 && ifMatch != "" {
				return &config.UsageError{Message: "--if-match guards a single issue and cannot be used with several keys."}
			}

			if len(args) == 1 {
				body, _ := json.Marshal(changes)
				resp, err := c.ChangeIssueWithBodyWithResponse(cmd.Context(), args[0], "application/json", bytes.NewReader(body),
					func(_ context.Context, req *http.Request) error {
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
				return printIssue(g, cmd, *resp.JSON200)
			}

			body, _ := json.Marshal(map[string]any{"keys": args, "changes": changes})
			resp, err := c.ChangeIssuesWithBodyWithResponse(cmd.Context(), "application/json", bytes.NewReader(body))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			for i, issue := range resp.JSON200.Items {
				if i > 0 {
					fmt.Fprintln(cmd.OutOrStdout())
				}
				render.Issue(cmd.OutOrStdout(), issue)
			}
			return nil
		},
	}

	cmd.Flags().StringVar(&title, "title", "", "the new title")
	cmd.Flags().StringVar(&descriptionFile, "description-file", "", "the description as Markdown, from a file or `-` for stdin")
	cmd.Flags().StringVar(&resultFile, "result-file", "", "the result as Markdown, from a file or `-` for stdin")
	cmd.Flags().IntVar(&priority, "priority", 0, "0 to 4")
	cmd.Flags().StringVar(&ready, "ready", "", "true or false")
	cmd.Flags().StringVar(&assignee, "assignee", "", "a name, or `none`")
	cmd.Flags().StringVar(&epic, "epic", "", "an epic key, or `none`")
	cmd.Flags().StringVar(&parent, "parent", "", "a parent issue key, or `none`")
	cmd.Flags().StringArrayVar(&labels, "label", nil, "the whole label set; repeatable, replaces what is there")
	cmd.Flags().StringVar(&status, "status", "", "backlog or todo: parking and unparking; every other move is an act")
	cmd.Flags().StringVar(&ifMatch, "if-match", "", "the updated_at as last read; the write is refused as stale when it moved")

	return cmd
}

// -------------------------------------------------------- delete, restore ----

func newIssueDelete(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "delete KEY [KEY...]",
		Short: "Soft-delete one or several issues; a bulk delete is all or none.",
		Args:  cobra.RangeArgs(1, 100),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			if len(args) == 1 {
				resp, err := c.DeleteIssueWithResponse(cmd.Context(), args[0])
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
			} else {
				resp, err := c.DeleteIssuesWithResponse(cmd.Context(), api.DeleteIssuesRequest{Keys: &args})
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
			}
			if g.json {
				if len(args) == 1 {
					return render.JSON(cmd.OutOrStdout(), map[string]any{"key": strings.ToUpper(args[0]), "deleted": true})
				}
				return render.JSON(cmd.OutOrStdout(), map[string]any{"keys": upper(args), "deleted": true})
			}
			for _, key := range upper(args) {
				fmt.Fprintf(cmd.OutOrStdout(), "%s deleted; `ha issue restore %s` brings it back.\n", key, key)
			}
			return nil
		},
	}
}

func upper(keys []string) []string {
	result := make([]string, len(keys))
	for i, key := range keys {
		result[i] = strings.ToUpper(key)
	}
	return result
}

func newIssueRestore(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "restore KEY",
		Short: "Back into whatever state it was in, without its claim.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RestoreIssueWithResponse(cmd.Context(), args[0])
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

// --------------------------------------------------------------- history ----

func newIssueHistory(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "history KEY",
		Short: "Every change, oldest first: who, when, which field, from what to what.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadHistoryWithResponse(cmd.Context(), args[0])
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

// --------------------------------------------------------- label, block ----

func newIssueLabel(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "label", Short: "One label on or off an issue."}
	cmd.AddCommand(
		&cobra.Command{
			Use: "add KEY NAME", Short: "Add a label, replacing another of its group.", Args: cobra.ExactArgs(2),
			RunE: func(cmd *cobra.Command, args []string) error {
				_, c, err := g.load()
				if err != nil {
					return err
				}
				resp, err := c.AddIssueLabelWithResponse(cmd.Context(), args[0], args[1])
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
				return printIssue(g, cmd, *resp.JSON200)
			},
		},
		&cobra.Command{
			Use: "remove KEY NAME", Short: "Remove a label.", Args: cobra.ExactArgs(2),
			RunE: func(cmd *cobra.Command, args []string) error {
				_, c, err := g.load()
				if err != nil {
					return err
				}
				resp, err := c.RemoveIssueLabelWithResponse(cmd.Context(), args[0], args[1])
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
				return printIssue(g, cmd, *resp.JSON200)
			},
		})
	return cmd
}

func newIssueBlock(g *globals) *cobra.Command {
	var by string
	cmd := &cobra.Command{
		Use:   "block KEY --by BLOCKER",
		Short: "Add a blocker: KEY waits for BLOCKER, across projects if need be; `cycle` when it would close one.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if by == "" {
				return &config.UsageError{Message: "--by BLOCKER: the issue KEY waits for."}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.AddBlockerWithResponse(cmd.Context(), args[0], by)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printIssue(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().StringVar(&by, "by", "", "the blocking issue")
	return cmd
}

func newIssueUnblock(g *globals) *cobra.Command {
	var by string
	cmd := &cobra.Command{
		Use:   "unblock KEY --by BLOCKER",
		Short: "Remove a blocker.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if by == "" {
				return &config.UsageError{Message: "--by BLOCKER: the blocker to remove."}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RemoveBlockerWithResponse(cmd.Context(), args[0], by)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printIssue(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().StringVar(&by, "by", "", "the blocking issue")
	return cmd
}
