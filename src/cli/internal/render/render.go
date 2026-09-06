// Package render is how ha prints for a person; --json prints the object as
// the API answered it.
package render

import (
	"encoding/json"
	"fmt"
	"io"
	"strings"
	"time"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
)

// JSON prints v as the API would, indented.
func JSON(w io.Writer, v any) error {
	enc := json.NewEncoder(w)
	enc.SetIndent("", "  ")
	enc.SetEscapeHTML(false)
	return enc.Encode(v)
}

// Summaries prints the slim issues as a table: key, priority, status, title.
func Summaries(w io.Writer, items []api.IssueSummary) {
	for _, item := range items {
		fmt.Fprintf(w, "%-10s P%d  %-11s  %s\n", item.Key, item.Priority, item.Status, item.Title)
	}
}

func Releases(w io.Writer, items []api.ReleaseSummary) {
	for _, release := range items {
		when := ""
		if release.PublishedAt != nil {
			when = release.PublishedAt.Format("2006-01-02")
		}
		fmt.Fprintf(w, "%-20s %-10s %4d  %s\n", release.Name, release.Status, release.Issues, when)
	}
}

func Release(w io.Writer, release api.Release) {
	fmt.Fprintf(w, "%s  %s\n", release.Name, release.Status)
	if release.PublishedAt != nil && release.PublishedBy != nil {
		fmt.Fprintf(w, "published: %s by %s\n", release.PublishedAt.Format("2006-01-02 15:04"), release.PublishedBy.Name)
	}
	if release.Description != "" {
		fmt.Fprintf(w, "\n%s\n", release.Description)
	}
	if len(release.Issues) > 0 {
		fmt.Fprintln(w, "\n## Issues")
	}
	releaseIssues(w, release.Issues)
}

func ReleaseNotes(w io.Writer, release api.Release) {
	if release.Description != "" {
		fmt.Fprintln(w, release.Description)
		if len(release.Issues) > 0 {
			fmt.Fprintln(w)
		}
	}
	releaseIssues(w, release.Issues)
}

func releaseIssues(w io.Writer, issues []api.IssueSummary) {
	for _, issue := range issues {
		prefix := "- "
		if issue.Parent != nil {
			prefix = "  - "
		}
		fmt.Fprintf(w, "%s%s %s\n", prefix, issue.Key, strings.TrimSpace(issue.Title))
	}
}

// Issue prints the complete issue: the head, the edges, then the epic's
// description and the issue's own as Markdown, then the conversation.
func Issue(w io.Writer, issue api.Issue) {
	fmt.Fprintf(w, "%s  %s\n", issue.Key, issue.Title)
	fmt.Fprintf(w, "status: %s  priority: %d  ready: %t", issue.Status, issue.Priority, issue.Ready)
	if issue.Claim != nil {
		fmt.Fprintf(w, "  claimed by: %s", issue.Claim.Holder.Name)
	}
	if issue.Assignee != nil {
		fmt.Fprintf(w, "  assignee: %s", issue.Assignee.Name)
	}
	if issue.Epic != nil {
		fmt.Fprintf(w, "  epic: %s", issue.Epic.Key)
	}
	if issue.Parent != nil {
		fmt.Fprintf(w, "  parent: %s", issue.Parent.Key)
	}
	if issue.Release != nil {
		fmt.Fprintf(w, "  release: %s", *issue.Release)
	}
	fmt.Fprintln(w)
	if len(issue.Labels) > 0 {
		names := make([]string, 0, len(issue.Labels))
		for _, l := range issue.Labels {
			names = append(names, l.Name)
		}
		fmt.Fprintf(w, "labels: %s\n", strings.Join(names, ", "))
	}
	if len(issue.BlockedBy) > 0 {
		fmt.Fprintf(w, "blocked by: %s\n", links(issue.BlockedBy))
	}
	if len(issue.Blocks) > 0 {
		fmt.Fprintf(w, "blocks: %s\n", links(issue.Blocks))
	}
	// The context package, widest first: what holds for the whole project, then
	// for the theme, then the ticket itself (VISION 15.5). The instructions are
	// printed here and nowhere else, because this is where the package arrives.
	if i := issue.ProjectContext.Instructions; i != nil && i.Body != "" {
		fmt.Fprintf(w, "\n## %s (%s/%s)\n\n%s\n", i.Title, issue.ProjectContext.Key, i.Slug, i.Body)
	}
	if issue.Epic != nil && issue.Epic.Description != "" {
		fmt.Fprintf(w, "\n## %s (%s)\n\n%s\n", issue.Epic.Title, issue.Epic.Key, issue.Epic.Description)
	}
	if issue.Description != "" {
		fmt.Fprintf(w, "\n%s\n", issue.Description)
	}
	// In full, not as a note that there is one to fetch: an extra fetch is
	// exactly what the package exists to spare (VISION 15.5), and a blocker's
	// outcome is what the work here starts from.
	for _, b := range issue.BlockedBy {
		if b.Result == nil || *b.Result == "" || b.Key == nil {
			continue
		}
		title := ""
		if b.Title != nil {
			title = "  " + *b.Title
		}
		fmt.Fprintf(w, "\n## What %s decided%s\n\n%s\n", *b.Key, title, *b.Result)
	}
	if len(issue.SubIssues) > 0 {
		fmt.Fprintln(w, "\n## Sub-issues")
		for _, child := range issue.SubIssues {
			fmt.Fprintf(w, "\n- %s  %s", child.Key, child.Title)
		}
		fmt.Fprintln(w)
	}
	if issue.Result != nil && *issue.Result != "" {
		fmt.Fprintf(w, "\n## Result\n\n%s\n", *issue.Result)
	}
	for _, q := range issue.Questions {
		fmt.Fprintf(w, "\n? %s (%s, %s)\n", q.Question, q.AskedBy.Name, q.AskedAt.Format("2006-01-02 15:04"))
		if q.Answer != nil && q.AnsweredBy != nil {
			fmt.Fprintf(w, "! %s (%s)\n", *q.Answer, q.AnsweredBy.Name)
		} else {
			fmt.Fprintln(w, "  (open)")
		}
	}
	for _, c := range issue.Comments {
		// Said "edited" where its author corrected it (ADR 0022): a comment
		// that is no longer what was first written says so here too, or the
		// console shows a correction as if it were the original.
		edited := ""
		if c.EditedAt != nil {
			edited = ", edited"
		}
		fmt.Fprintf(w, "\n> %s (%s, %s%s)\n", c.Body, c.Author.Name, c.CreatedAt.Format("2006-01-02 15:04"), edited)
	}
}

func links(edges []api.BlockerLink) string {
	parts := make([]string, 0, len(edges))
	for _, e := range edges {
		key := "(hidden)"
		if e.Key != nil {
			key = *e.Key
		}
		state := "closed"
		if e.Open {
			state = "open"
		}
		parts = append(parts, key+" ("+state+")")
	}
	return strings.Join(parts, ", ")
}

// History prints the entries oldest first: when, who, what, from what to what.
func History(w io.Writer, entries []api.HistoryEntry) {
	for _, e := range entries {
		from, to := value(e.OldValue), value(e.NewValue)
		line := fmt.Sprintf("%s  %-16s %-12s", e.At.Format("2006-01-02 15:04:05"), e.Actor.Name, e.Field)
		switch {
		case from == "" && to == "":
		case from == "":
			line += "  → " + to
		case to == "":
			line += "  " + from + " →"
		default:
			line += "  " + from + " → " + to
		}
		if e.Note != nil && *e.Note != "" {
			line += "  (" + *e.Note + ")"
		}
		fmt.Fprintln(w, line)
	}
}

// value renders a history value: a string as itself, a rendered identity by its name.
func value(v any) string {
	switch t := v.(type) {
	case nil:
		return ""
	case string:
		return t
	case map[string]any:
		if name, ok := t["name"].(string); ok {
			return name
		}
	}
	b, _ := json.Marshal(v)
	return string(b)
}

// Questions prints the open questions of a project: the issue, the question, who asked.
func Questions(w io.Writer, items []api.ProjectQuestion) {
	for _, q := range items {
		state := "open"
		if q.Answer != nil {
			state = "answered by " + q.AnsweredBy.Name
		}
		fmt.Fprintf(w, "%s  %-10s %s\n    %s  (%s, %s; %s)\n", q.Id, q.Issue.Key, q.Issue.Title, q.Question, q.AskedBy.Name, q.AskedAt.Format("2006-01-02 15:04"), state)
	}
}

// NeedsYou prints the four groups in the order a human should clear them.
func NeedsYou(w io.Writer, items []api.NeedsYouItem) {
	headings := map[api.NeedsYouBecause]string{
		api.NeedsYouBecauseQuestion: "Open questions",
		api.NeedsYouBecauseReview:   "In review",
		api.NeedsYouBecauseUnready:  "Not ready",
		api.NeedsYouBecauseStuck:    "Stuck",
	}
	var previous api.NeedsYouBecause
	for index, item := range items {
		if index == 0 || item.Because != previous {
			if index > 0 {
				fmt.Fprintln(w)
			}
			fmt.Fprintf(w, "%s:\n", headings[item.Because])
			previous = item.Because
		}
		Summaries(w, []api.IssueSummary{item.Issue})
	}
}

// Project prints one project with its switches.
func Project(w io.Writer, p api.Project) {
	fmt.Fprintf(w, "%s  %s\n", p.Key, p.Name)
	fmt.Fprintf(w, "triage required: %t  review required: %t", p.TriageRequired, p.ReviewRequired)
	if p.InstructionsPage != nil {
		fmt.Fprintf(w, "  instructions: %s", *p.InstructionsPage)
	}
	fmt.Fprintln(w)
}

// ProjectLine prints one project as a list line.
func ProjectLine(w io.Writer, p api.Project) {
	flags := ""
	if p.TriageRequired {
		flags += " triage"
	}
	if p.ReviewRequired {
		flags += " review"
	}
	fmt.Fprintf(w, "%-10s %s%s\n", p.Key, p.Name, flags)
}

// Labels prints labels with their group and description — the project's
// schema, which an agent reads before it labels anything.
func Labels(w io.Writer, labels []api.Label) {
	for _, l := range labels {
		group := ""
		if l.Group != nil {
			group = *l.Group
		}
		description := ""
		if l.Description != nil {
			description = *l.Description
		}
		fmt.Fprintf(w, "%-24s %-12s %s\n", l.Name, group, description)
	}
}

// Epic prints the complete epic: the head, the progress, the living document.
func Epic(w io.Writer, e api.Epic) {
	fmt.Fprintf(w, "%s  %s\n", e.Key, e.Title)
	fmt.Fprintf(w, "status: %s  progress: %s  author: %s\n", e.Status, progress(e.Progress), e.Author.Name)
	if len(e.Labels) > 0 {
		names := make([]string, 0, len(e.Labels))
		for _, l := range e.Labels {
			names = append(names, l.Name)
		}
		fmt.Fprintf(w, "labels: %s\n", strings.Join(names, ", "))
	}
	if e.Description != "" {
		fmt.Fprintf(w, "\n%s\n", e.Description)
	}
}

// EpicSummaries prints epics as a table with their progress.
func EpicSummaries(w io.Writer, items []api.EpicSummary) {
	for _, e := range items {
		fmt.Fprintf(w, "%-10s %-7s %-22s %s\n", e.Key, e.Status, progress(e.Progress), e.Title)
	}
}

// Page prints the head — where it lives, what it is called, when it last moved
// — and then the Markdown exactly as it is stored, so that the output can be
// piped straight back into `--body-file -`.
func Page(w io.Writer, p api.Page) {
	fmt.Fprintf(w, "%s/%s  %s\n", p.Project, p.Slug, p.Title)
	fmt.Fprintf(w, "updated: %s by %s  author: %s\n", p.UpdatedAt.Format(time.RFC3339), p.UpdatedBy.Name, p.Author.Name)
	if len(p.Labels) > 0 {
		names := make([]string, 0, len(p.Labels))
		for _, l := range p.Labels {
			names = append(names, l.Name)
		}
		fmt.Fprintf(w, "labels: %s\n", strings.Join(names, ", "))
	}
	if p.Body != "" {
		fmt.Fprintf(w, "\n%s\n", p.Body)
	}
}

// PageSummaries prints the flat wiki: the address, when it last moved, the title.
func PageSummaries(w io.Writer, items []api.PageSummary) {
	for _, p := range items {
		fmt.Fprintf(w, "%-24s %-10s %s\n", p.Slug, p.UpdatedAt.Format("2006-01-02"), p.Title)
	}
}

// progress spells the counts the way VISION 7 does: `5 of 7 closed · 4 done · 1 canceled`.
func progress(p api.Progress) string {
	return fmt.Sprintf("%d of %d closed · %d done · %d canceled", p.Closed, p.Total, p.Done, p.Canceled)
}

// Me prints the caller as GET /me answers.
func Me(w io.Writer, me api.Me) {
	role := ""
	if me.Administrator {
		role = "  administrator"
	}
	fmt.Fprintf(w, "%s (%s)%s\n", me.Name, me.Kind, role)
	if me.Owner != nil {
		fmt.Fprintf(w, "owner: %s\n", me.Owner.Name)
	}
	fmt.Fprintf(w, "token: %s…  since %s\n", me.Token.Prefix, me.Token.CreatedAt.Format("2006-01-02"))
	metadata(w, me.Metadata, me.MetadataReportedAt)
}

func Agents(w io.Writer, agents []api.AgentSummary) {
	for _, a := range agents {
		state := a.Token.Prefix + "…"
		if a.Token.RevokedAt != nil {
			state = "revoked " + a.Token.RevokedAt.Format("2006-01-02")
		}
		fmt.Fprintf(w, "%-36s %-24s owner: %-16s %s", a.Id, a.Name, a.Owner.Name, state)
		if summary := metadataSummary(a.Metadata); summary != "" {
			fmt.Fprintf(w, "  metadata: %s", summary)
			if a.MetadataReportedAt != nil {
				fmt.Fprintf(w, " (%s)", a.MetadataReportedAt.Format("2006-01-02"))
			}
		}
		fmt.Fprintln(w)
	}
}

func Agent(w io.Writer, a api.AgentSummary) {
	fmt.Fprintf(w, "%s (%s)\nid: %s\nowner: %s\n", a.Name, a.Kind, a.Id, a.Owner.Name)
	state := a.Token.Prefix + "…  since " + a.Token.CreatedAt.Format("2006-01-02")
	if a.Token.RevokedAt != nil {
		state = "revoked " + a.Token.RevokedAt.Format("2006-01-02")
	}
	fmt.Fprintf(w, "token: %s\n", state)
	metadata(w, a.Metadata, a.MetadataReportedAt)
}

func metadata(w io.Writer, m *api.AgentMetadata, at *time.Time) {
	if m == nil || at == nil {
		return
	}
	fmt.Fprintf(w, "metadata: %s", at.Format("2006-01-02 15:04"))
	for _, field := range []struct {
		name  string
		value *string
	}{{"kind", m.Kind}, {"harness", m.Harness}, {"environment", m.Environment}, {"version", m.Version}} {
		if field.value != nil {
			fmt.Fprintf(w, "  %s: %s", field.name, *field.value)
		}
	}
	fmt.Fprintln(w)
}

func metadataSummary(m *api.AgentMetadata) string {
	if m == nil {
		return ""
	}
	parts := []string{}
	for _, field := range []struct {
		name  string
		value *string
	}{{"kind", m.Kind}, {"harness", m.Harness}, {"environment", m.Environment}, {"version", m.Version}} {
		if field.value != nil {
			parts = append(parts, field.name+"="+*field.value)
		}
	}
	return strings.Join(parts, ", ")
}

// Reasons prints why nothing was handed out, in the words of VISION 10.
func Reasons(w io.Writer, r api.Reasons) {
	fmt.Fprintf(w, "nothing workable: %d blocked, %d waiting for an answer, %d in progress, %d in review, %d parked, %d not ready, %d assigned elsewhere\n",
		r.Blocked, r.WaitingForAnswer, r.InProgress, r.InReview, r.Parked, r.NotReady, r.AssignedElsewhere)
}
