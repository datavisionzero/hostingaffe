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

// Machine prints the complete machine: what it is, where it is, what it is made
// of, and who touched it last. A field nobody has filled in is left out rather
// than printed empty — a record of a machine is expected to be incomplete
// (VISION 7), and empty labels would be most of the screen.
func Machine(w io.Writer, m api.Machine) {
	fmt.Fprintf(w, "%s  %s\n", m.Key, m.Name)
	line(w,
		said("kind", string(m.Kind)),
		said("status", string(m.Status)),
		maybe("arch", (*string)(m.Arch)),
		maybe("host", m.Host))
	line(w, maybe("provider", m.Provider), maybe("plan", m.Plan), maybe("location", m.Location))
	line(w, maybe("hostname", m.Hostname), maybe("ssh", m.Ssh))
	line(w, maybe("ipv4", m.Ipv4), maybe("ipv6", m.Ipv6), maybe("private ip", m.PrivateIp))
	line(w, maybe("os", m.Os), maybe("cpu", m.Cpu), maybe("memory", m.Memory), maybe("disk", m.Disk))
	if m.MeasuredAt != nil {
		line(w, said("measured", m.MeasuredAt.Format(time.RFC3339)))
	}
	touched(w, m.UpdatedAt, m.UpdatedBy, m.CreatedBy)
	body(w, m.Description)
}

// MachineSummaries prints the fleet: the key, what kind of thing it is, whether
// it is still there, where it stands, and what it is called.
func MachineSummaries(w io.Writer, items []api.MachineSummary) {
	for _, m := range items {
		fmt.Fprintf(w, "%-16s %-9s %-8s %-12s %-12s %-6s %-10s %s\n",
			m.Key, m.Kind, m.Status, or(m.Provider), or(m.Location), or((*string)(m.Arch)),
			m.UpdatedAt.Format("2006-01-02"), m.Name)
	}
}

// Software prints the complete software. It carries no version: versions belong
// to deployments (CONTEXT.md, Software).
func Software(w io.Writer, s api.Software) {
	fmt.Fprintf(w, "%s  %s\n", s.Key, s.Name)
	line(w, maybe("image", s.Image))
	line(w, maybe("homepage", s.Homepage), maybe("repository", s.Repository))
	touched(w, s.UpdatedAt, s.UpdatedBy, s.CreatedBy)
	body(w, s.Description)
}

func SoftwareSummaries(w io.Writer, items []api.SoftwareSummary) {
	for _, s := range items {
		fmt.Fprintf(w, "%-16s %-32s %-10s %s\n",
			s.Key, or(s.Image), s.UpdatedAt.Format("2006-01-02"), s.Name)
	}
}

// Installation prints the complete installation: the two keys that make it one,
// the answers to the questions every installation answers, and the three
// decisions that are the point of asking.
func Installation(w io.Writer, i api.Installation) {
	fmt.Fprintf(w, "%s  %s\n", i.Key, i.Name)
	line(w, said("machine", i.Machine), said("software", i.Software), maybe("version", i.Version))
	line(w, said("environment", string(i.Environment)), said("role", string(i.Role)), said("status", string(i.Status)))
	line(w,
		said("backup", string(i.Backup)),
		said("monitoring", string(i.Monitoring)),
		said("logging", string(i.Logging)))
	line(w, maybe("path", i.Path))
	line(w, said("ports", Ports(i.Ports)))
	line(w, said("urls", strings.Join(i.Urls, ", ")))
	line(w, said("secrets", strings.Join(i.Secrets, ", ")))
	touched(w, i.UpdatedAt, i.UpdatedBy, i.CreatedBy)
	body(w, i.Description)
}

func InstallationSummaries(w io.Writer, items []api.InstallationSummary) {
	for _, i := range items {
		fmt.Fprintf(w, "%-16s %-14s %-14s %-12s %-12s %-8s %-10s %-18s %-10s %s\n",
			i.Key, i.Machine, i.Software, i.Environment, i.Role, i.Status, or(i.Version),
			fmt.Sprintf("%s/%s/%s", i.Backup, i.Monitoring, i.Logging),
			i.UpdatedAt.Format("2006-01-02"), i.Name)
	}
}

// Ports spells a list of ports the way a person writes and reads one —
// `443/tcp:public` — which is the same spelling a history row carries
// (CONTEXT.md, Installation).
func Ports(ports []api.Port) string {
	spelled := make([]string, 0, len(ports))
	for _, p := range ports {
		spelled = append(spelled, fmt.Sprintf("%d/%s:%s", p.Port, p.Protocol, p.Scope))
	}
	return strings.Join(spelled, ", ")
}

// History prints what the instance recorded, oldest first: when, who, which
// field, from what to what, and the note that came with the change. A row with
// neither value — a text that records that it changed, and the acts named
// `created`, `deleted` and `restored` — prints the field and stops there.
func History(w io.Writer, entries []api.HistoryEntry) {
	for _, e := range entries {
		fmt.Fprintf(w, "%s  %-16s %-14s", e.At.Format("2006-01-02 15:04"), e.Actor.Name, e.Field)
		if e.OldValue != nil || e.NewValue != nil {
			fmt.Fprintf(w, " %s → %s", or(e.OldValue), or(e.NewValue))
		}
		if e.Note != nil {
			fmt.Fprintf(w, "  (%s)", *e.Note)
		}
		fmt.Fprintln(w)
	}
}

// A field and what it says, for the lines that print only what is filled in.
type field struct{ label, value string }

func said(label, value string) field { return field{label, value} }

func maybe(label string, value *string) field {
	if value == nil {
		return field{label, ""}
	}
	return field{label, *value}
}

// line prints the fields that have a value and nothing at all when none of them
// does, so a machine nobody has measured says nothing about measuring rather
// than printing a row of empty labels.
func line(w io.Writer, fields ...field) {
	said := make([]string, 0, len(fields))
	for _, f := range fields {
		if f.value != "" {
			said = append(said, f.label+": "+f.value)
		}
	}
	if len(said) > 0 {
		fmt.Fprintln(w, strings.Join(said, "  "))
	}
}

// touched is the last line of every record: when it last moved, who moved it,
// and who put it there in the first place.
func touched(w io.Writer, at time.Time, by, author api.IdentityRef) {
	fmt.Fprintf(w, "updated: %s by %s  author: %s\n", at.Format(time.RFC3339), by.Name, author.Name)
}

// body prints the description after a blank line, as it is stored, so the
// output can be piped straight back into `--description-file -`.
func body(w io.Writer, text string) {
	if text != "" {
		fmt.Fprintf(w, "\n%s\n", text)
	}
}

// or prints nothing as a dash: a column with no value is not a column with a
// value that is empty.
func or(value *string) string {
	if value == nil || *value == "" {
		return "-"
	}
	return *value
}

// Deployment prints the complete record: the version it put there, the one
// before it, when it happened and who recorded it, and the file revisions that
// were current when it went live. `previous` and `files` are derived by `at`,
// never by the order of recording (CONTEXT.md, Deployment).
func Deployment(w io.Writer, d api.Deployment) {
	fmt.Fprintf(w, "%s #%d  %s\n", d.Installation, d.Number, d.Version)
	fmt.Fprintf(w, "at: %s by %s\n", d.At.Format(time.RFC3339), d.By.Name)
	line(w, maybe("previous", d.Previous), maybe("ticket", d.Ticket))
	line(w, maybe("ref", d.Ref))
	line(w, said("files", revisions(d.Files)))

	fmt.Fprintf(w, "recorded: %s", d.CreatedAt.Format(time.RFC3339))
	// A deployment is corrected, not edited, so the second timestamp is said
	// only where there was a correction to say it about.
	if d.UpdatedAt.After(d.CreatedAt) {
		fmt.Fprintf(w, "  corrected: %s by %s", d.UpdatedAt.Format(time.RFC3339), d.UpdatedBy.Name)
	}
	fmt.Fprintln(w)

	body(w, d.Note)
}

// DeploymentSummaries prints the history of an installation, newest by `at`
// first — the order everything derived uses, so the list and the version agree.
func DeploymentSummaries(w io.Writer, items []api.DeploymentSummary) {
	for _, d := range items {
		fmt.Fprintf(w, "%-5s %-14s %-14s %-17s %-16s %-10s %s\n",
			fmt.Sprintf("#%d", d.Number), d.Version, or(d.Previous),
			d.At.Format("2006-01-02 15:04"), d.By.Name, or(d.Ticket), or(d.Ref))
	}
}

// revisions spells the file revisions a deployment went live with as
// `compose.yml@4`: the path and the revision that was current, which is what a
// `ha files get --revision` asks for.
func revisions(files []api.DeploymentFile) string {
	spelled := make([]string, 0, len(files))
	for _, f := range files {
		spelled = append(spelled, fmt.Sprintf("%s@%d", f.Path, f.Revision))
	}
	return strings.Join(spelled, ", ")
}

// File prints the head of a file and not its content: what a write produced,
// where it sits, and who put it there. `ha files get` is the content, so that
// it can be redirected into a file without a head on top of it.
func File(w io.Writer, f api.File) {
	fmt.Fprintf(w, "%s  revision %d  %s\n", f.Path, f.Revision, Anchor(&f.Owner))
	line(w, said("executable", executable(f.Executable)), said("bytes", fmt.Sprint(len(f.Content))))
	fmt.Fprintf(w, "updated: %s by %s  author: %s\n",
		f.UpdatedAt.Format(time.RFC3339), f.UpdatedBy.Name, f.CreatedBy.Name)
}

// FileSummaries prints what is under an owner: the path, the revision it is at,
// whether it is executable, and when it last moved.
func FileSummaries(w io.Writer, items []api.FileSummary) {
	for _, f := range items {
		fmt.Fprintf(w, "%-40s %-10s %-4s %-16s %s\n",
			f.Path, fmt.Sprintf("revision %d", f.Revision), executable(f.Executable),
			f.UpdatedBy.Name, f.UpdatedAt.Format("2006-01-02 15:04"))
	}
}

// FileRevisions prints every write of one file, newest first, without what each
// of them wrote: one of them is read by asking for the file with `--revision`.
func FileRevisions(w io.Writer, items []api.FileRevision) {
	for _, r := range items {
		fmt.Fprintf(w, "%-10s %-4s %-16s %s\n",
			fmt.Sprintf("revision %d", r.Revision), executable(r.Executable),
			r.By.Name, r.At.Format("2006-01-02 15:04"))
	}
}

// executable says the one mode bit there is, and says nothing where it is not
// set: a file that is not executable is the ordinary case.
func executable(set bool) string {
	if set {
		return "+x"
	}
	return ""
}
