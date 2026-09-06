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

// Page prints the head — the address, what it is called, when it last moved —
// and then the Markdown exactly as it is stored, so that the output can be
// piped straight back into `--body-file -`.
func Page(w io.Writer, p api.Page) {
	fmt.Fprintf(w, "%s  %s\n", p.Slug, p.Title)
	fmt.Fprintf(w, "kind: %s  hangs on: %s\n", p.Kind, Anchor(p.AttachedTo))
	fmt.Fprintf(w, "updated: %s by %s  author: %s\n", p.UpdatedAt.Format(time.RFC3339), p.UpdatedBy.Name, p.Author.Name)
	if p.Body != "" {
		fmt.Fprintf(w, "\n%s\n", p.Body)
	}
}

// PageSummaries prints the flat wiki: the address, its kind, what it hangs on,
// when it last moved, the title.
func PageSummaries(w io.Writer, items []api.PageSummary) {
	for _, p := range items {
		fmt.Fprintf(w, "%-24s %-9s %-26s %-10s %s\n",
			p.Slug, p.Kind, Anchor(p.AttachedTo), p.UpdatedAt.Format("2006-01-02"), p.Title)
	}
}

// Anchor names what a record hangs on as the kind and the key together, because
// the machine `caddy` and the software `caddy` are different things and a key
// alone would not say which. Nothing to hang on prints as a dash: a page of the
// instance as a whole is not a page with a field missing.
func Anchor(a *api.Anchor) string {
	if a == nil {
		return "-"
	}
	return string(a.Kind) + " " + a.Key
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
