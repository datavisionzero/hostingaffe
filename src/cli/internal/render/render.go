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
	// The machine's own ports and no installation's: SSH, a Wireguard endpoint.
	// An empty list says nothing about the machine rather than "none".
	line(w, said("ports", Ports(m.Ports)))
	line(w, maybe("os", m.Os), maybe("cpu", m.Cpu), maybe("memory", m.Memory), maybe("disk", m.Disk))
	// `measured` is when a person last checked the facts; `last seen` is when
	// the machine last spoke for itself. The two mean different things, and
	// standing beside each other is what makes that readable (CONTEXT.md).
	fields := []field{}
	if m.MeasuredAt != nil {
		fields = append(fields, said("measured", m.MeasuredAt.Format(time.RFC3339)))
	}
	if m.LastSeen != nil {
		fields = append(fields, said("last seen", Ago(time.Now(), *m.LastSeen)+" ("+m.LastSeen.Format(time.RFC3339)+")"))
	}
	if m.RebootRequired != nil && *m.RebootRequired {
		fields = append(fields, said("restart", "pending"))
	}
	line(w, fields...)
	touched(w, m.UpdatedAt, m.UpdatedBy, m.CreatedBy)

	// What the record above and the machine's own last word disagree about. It
	// is here, at the machine, because that is where somebody reads the fields
	// it contradicts (ADR 0015).
	Drift(w, m.Drift)

	body(w, m.Description)
}

// MachineSummaries prints the fleet: the key, what kind of thing it is, whether
// it is still there, where it stands, and what it is called.
func MachineSummaries(w io.Writer, items []api.MachineSummary) {
	now := time.Now()
	for _, m := range items {
		// No threshold, no colour, no word of judgement: when it last spoke,
		// and the person reads it (VISION 5).
		seen := "-"
		if m.LastSeen != nil {
			seen = Ago(now, *m.LastSeen)
		}
		// The one column worth reading down ten machines on a Friday
		// afternoon: which of them are waiting for a restart. A word, and no
		// colour and no threshold.
		restart := ""
		if m.RebootRequired != nil && *m.RebootRequired {
			restart = "restart"
		}
		fmt.Fprintf(w, "%-16s %-9s %-8s %-12s %-12s %-6s %-15s %-8s %-10s %s\n",
			m.Key, m.Kind, m.Status, or(m.Provider), or(m.Location), or((*string)(m.Arch)),
			seen, restart, m.UpdatedAt.Format("2006-01-02"), m.Name)
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
// the answers to the questions every installation answers, the three decisions
// that are the point of asking, and the one edge from both ends — what it needs,
// and what needs it. The second is what a person asks before an intervention,
// and it is the instance's to derive (ADR 0014).
func Installation(w io.Writer, i api.Installation) {
	fmt.Fprintf(w, "%s  %s\n", i.Key, i.Name)
	line(w, said("machine", i.Machine), said("software", i.Software), maybe("version", i.Version))
	line(w, said("environment", string(i.Environment)), said("role", string(i.Role)), said("status", string(i.Status)))
	line(w,
		said("backup", string(i.Backup)),
		said("monitoring", string(i.Monitoring)),
		said("logging", string(i.Logging)))
	line(w, maybe("path", i.Path), maybe("data", i.Data))
	line(w, said("ports", Ports(i.Ports)))
	line(w, said("urls", strings.Join(i.Urls, ", ")))
	line(w, said("secrets", Secrets(i.Secrets)))
	line(w, said("depends on", strings.Join(i.DependsOn, ", ")))
	line(w, said("needed by", strings.Join(i.NeededBy, ", ")))
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

// Secrets spells a list of secrets the way a person writes and reads one —
// `POSTGRES_PASSWORD@/opt/compose/logaffe/.env.runtime`, and the name alone
// where nobody has said where it lies. The value is nowhere near it
// (CONTEXT.md, Installation).
func Secrets(secrets []api.Secret) string {
	spelled := make([]string, 0, len(secrets))
	for _, s := range secrets {
		if s.Path != nil && *s.Path != "" {
			spelled = append(spelled, fmt.Sprintf("%s@%s", s.Name, *s.Path))
			continue
		}
		spelled = append(spelled, s.Name)
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
	line(w, maybe("directory", f.Directory),
		said("executable", executable(f.Executable)), said("bytes", fmt.Sprint(len(f.Content))))
	fmt.Fprintf(w, "updated: %s by %s  author: %s\n",
		f.UpdatedAt.Format(time.RFC3339), f.UpdatedBy.Name, f.CreatedBy.Name)
}

// FileSummaries prints what is under an owner: the path, where it lies on the
// machine, how big it is, the revision it is at, whether it is executable, and
// when it last moved.
//
// The directory is a column only where there is one to print. A machine's files
// each say where on the machine they lie, and an installation's say nothing,
// because the installation's own path already said it for all of them — so a
// column of empty cells is what an installation would get.
func FileSummaries(w io.Writer, items []api.FileSummary) {
	placed := false
	for _, f := range items {
		if f.Directory != nil && *f.Directory != "" {
			placed = true
		}
	}

	for _, f := range items {
		where := ""
		if placed {
			if f.Directory != nil {
				where = *f.Directory
			}
			where = fmt.Sprintf("%-30s ", where)
		}
		fmt.Fprintf(w, "%-40s %s%10s %-10s %-4s %-16s %s\n",
			f.Path, where, size(f.Size), fmt.Sprintf("revision %d", f.Revision), executable(f.Executable),
			f.UpdatedBy.Name, f.UpdatedAt.Format("2006-01-02 15:04"))
	}
}

// size spells a file's size the way the instance counts it: exactly, in bytes
// of UTF-8, because the number a write is refused against is a number of bytes
// and a rounded one would answer "does this still fit?" with a different
// figure.
func size(bytes int32) string {
	return fmt.Sprintf("%d B", bytes)
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

// SearchHits prints what was found and where: the kind, the address, what it is
// called, and which surface of it the words matched. A deployment carries its
// number, a file and a page carry what they belong to.
//
// A machine's file is addressed by the whole place it lies, directory and path
// together, because the directory is one of the surfaces the search reads and a
// hit that answered "/etc/systemd/system" with a bare name would not say what
// it answered with (ADR 0008, 0012). An installation's file has no directory of
// its own: the installation's path already said it for all of them.
func SearchHits(w io.Writer, hits []api.SearchHit) {
	for _, hit := range hits {
		address := hit.Key
		if hit.Number != nil {
			address = fmt.Sprintf("%s #%d", hit.Key, *hit.Number)
		}
		if hit.Directory != nil && *hit.Directory != "" {
			address = strings.TrimRight(*hit.Directory, "/") + "/" + hit.Key
		}
		fmt.Fprintf(w, "%-13s %-40s %-10s %-26s %s\n",
			hit.Kind, address, hit.Where, Anchor(hit.Owner), hit.Name)
	}
}

// Ago is how long ago something happened, as a person reads it: "12 minutes
// ago", "6 days ago". No threshold and no judgement — `ha` says when, never
// "stale" and never "silent", because a line the product drew would be the
// wrong one for the next host (VISION 5).
func Ago(now, at time.Time) string {
	d := now.Sub(at)
	if d < 0 {
		return "just now"
	}
	switch {
	case d < time.Minute:
		return "just now"
	case d < 2*time.Minute:
		return "1 minute ago"
	case d < time.Hour:
		return fmt.Sprintf("%d minutes ago", int(d.Minutes()))
	case d < 2*time.Hour:
		return "1 hour ago"
	case d < 24*time.Hour:
		return fmt.Sprintf("%d hours ago", int(d.Hours()))
	case d < 48*time.Hour:
		return "1 day ago"
	default:
		return fmt.Sprintf("%d days ago", int(d.Hours()/24))
	}
}

// MachineToken prints what a person asks when a machine has gone quiet: is
// there a key, which one, who gave it out, and when it was last used. Never a
// secret — only the hash is kept (ADR 0016).
func MachineToken(w io.Writer, key string, t api.MachineToken) {
	if !t.Present && t.RevokedAt == nil {
		fmt.Fprintf(w, "%s has no token, and has never had one.\n", key)
		return
	}

	now := time.Now()
	if t.Present {
		fmt.Fprintf(w, "%s  token %s…\n", key, *t.Prefix)
	} else {
		fmt.Fprintf(w, "%s  token %s… revoked\n", key, *t.Prefix)
	}

	fields := []field{said("issued", t.IssuedAt.Format(time.RFC3339))}
	if t.IssuedBy != nil {
		fields = append(fields, said("by", t.IssuedBy.Name))
	}
	line(w, fields...)

	if t.LastUsedAt != nil {
		line(w, said("last used", Ago(now, *t.LastUsedAt)+" ("+t.LastUsedAt.Format(time.RFC3339)+")"))
	} else {
		line(w, said("last used", "never"))
	}

	if t.RevokedAt != nil {
		revoked := []field{said("revoked", t.RevokedAt.Format(time.RFC3339))}
		if t.RevokedBy != nil {
			revoked = append(revoked, said("by", t.RevokedBy.Name))
		}
		line(w, revoked...)
	}
}

// Bytes is a size as a person reads it: GB and MB, one decimal where that says
// something. The report carries bytes because the instance stores numbers; this
// is the rendering, and `--json` is where the numbers are.
func Bytes(n int64) string {
	const unit = 1000
	switch {
	case n < unit:
		return fmt.Sprintf("%dB", n)
	case n < unit*unit:
		return fmt.Sprintf("%.0fkB", float64(n)/unit)
	case n < unit*unit*unit:
		return fmt.Sprintf("%.0fMB", float64(n)/(unit*unit))
	case n < unit*unit*unit*unit:
		return fmt.Sprintf("%.1fGB", float64(n)/(unit*unit*unit))
	default:
		return fmt.Sprintf("%.1fTB", float64(n)/(unit*unit*unit*unit))
	}
}

// Uptime is how long the machine has been up, in the two units that say it.
func Uptime(seconds int64) string {
	days := seconds / 86400
	hours := (seconds % 86400) / 3600
	if days > 0 {
		return fmt.Sprintf("%dd %dh", days, hours)
	}
	return fmt.Sprintf("%dh %dm", hours, (seconds%3600)/60)
}

// Report prints what a machine said about itself: the sections one under the
// other, sizes in what a person reads, times relative with the exact one beside
// them. A section the collector could not determine stands there with its
// reason rather than being passed over in silence.
func Report(w io.Writer, r api.Report) {
	now := time.Now()

	// The line that answers the question somebody came with.
	fmt.Fprintf(w, "%s  report %d  received %s (%s)\n",
		r.Machine, r.Number, Ago(now, r.ReceivedAt), r.ReceivedAt.Format(time.RFC3339))

	agent := ""
	if r.Agent != nil {
		agent = "ha " + *r.Agent
	}
	line(w, said("collected", r.CollectedAt.Format(time.RFC3339)), said("by", agent))

	if h := r.Host; h != nil {
		fmt.Fprintln(w)
		line(w, maybe("hostname", h.Hostname), maybe("os", h.Os), maybe("kernel", h.Kernel), maybe("arch", h.Arch))
		fields := []field{}
		if h.UptimeSeconds != nil {
			fields = append(fields, said("up", Uptime(*h.UptimeSeconds)))
		}
		if h.Load1 != nil && h.Load5 != nil && h.Load15 != nil {
			fields = append(fields, said("load", fmt.Sprintf("%.2f %.2f %.2f", *h.Load1, *h.Load5, *h.Load15)))
		}
		line(w, fields...)
	}

	if m := r.Memory; m != nil {
		fields := []field{}
		if m.TotalBytes != nil && m.UsedBytes != nil {
			fields = append(fields, said("memory", fmt.Sprintf("%s of %s used", Bytes(*m.UsedBytes), Bytes(*m.TotalBytes))))
		} else if m.TotalBytes != nil {
			fields = append(fields, said("memory", Bytes(*m.TotalBytes)))
		}
		if m.SwapTotalBytes != nil && *m.SwapTotalBytes > 0 && m.SwapUsedBytes != nil {
			fields = append(fields, said("swap", fmt.Sprintf("%s of %s", Bytes(*m.SwapUsedBytes), Bytes(*m.SwapTotalBytes))))
		}
		line(w, fields...)
	}

	if r.Disks != nil && len(*r.Disks) > 0 {
		fmt.Fprintln(w)
		for _, disk := range *r.Disks {
			percent := "  -"
			if disk.Percent != nil {
				percent = fmt.Sprintf("%3d%%", *disk.Percent)
			}
			size := ""
			if disk.UsedBytes != nil && disk.SizeBytes != nil {
				size = fmt.Sprintf("%s of %s", Bytes(*disk.UsedBytes), Bytes(*disk.SizeBytes))
			}
			fmt.Fprintf(w, "%s  %-24s %-20s %s\n", percent, or(disk.Mount), size, or(disk.Device))
		}
	}

	if r.Containers != nil {
		fmt.Fprintln(w)
		running := 0
		for _, container := range *r.Containers {
			if container.State != nil && *container.State == "running" {
				running++
			}
		}
		fmt.Fprintf(w, "containers: %d of %d running\n", running, len(*r.Containers))
		for _, container := range *r.Containers {
			state := or(container.State)
			if container.Health != nil && *container.Health != "" {
				state += " (" + *container.Health + ")"
			}
			started := "-"
			if container.StartedAt != nil {
				started = Ago(now, *container.StartedAt)
			}
			restarts := "-"
			if container.Restarts != nil {
				restarts = fmt.Sprintf("%d", *container.Restarts)
			}
			fmt.Fprintf(w, "  %-20s %-44s %-20s %-16s restarts: %s\n",
				or(container.Name), or(container.Image), state, started, restarts)
		}
	}

	if r.Listening != nil && len(*r.Listening) > 0 {
		fmt.Fprintln(w)
		fmt.Fprintf(w, "listening: %d ports\n", len(*r.Listening))
		for _, one := range *r.Listening {
			port := "-"
			if one.Port != nil {
				port = fmt.Sprintf("%d/%s", *one.Port, or((*string)(one.Protocol)))
			}
			// No process, and nothing that hints at one: what `ha report
			// collect` prints is what leaves the host, and this is all of it.
			fmt.Fprintf(w, "  %-12s %s\n", port, or((*string)(one.Binding)))
		}
	}

	if r.Updates != nil && r.Updates.RebootRequired != nil {
		fmt.Fprintln(w)
		if *r.Updates.RebootRequired {
			fmt.Fprintln(w, "restart: the machine is waiting for one")
		} else {
			fmt.Fprintln(w, "restart: none pending")
		}
	}

	// What the collector could not determine is said, not swallowed: a report
	// that quietly left something out would be read as a machine that has it
	// not.
	if len(r.Missing) > 0 {
		fmt.Fprintln(w)
		for _, missing := range r.Missing {
			fmt.Fprintf(w, "%s: not determined (%s)\n", or(missing.Section), or(missing.Reason))
		}
	}

	Drift(w, r.Drift)
}

// ReportSummaries prints the series: one line per report, enough to see when
// something changed and then look into that one with `ha report show --number`.
func ReportSummaries(w io.Writer, page api.ReportPage) {
	now := time.Now()
	for _, r := range page.Reports {
		containers := "-"
		if r.ContainersTotal != nil && r.ContainersRunning != nil {
			containers = fmt.Sprintf("%d/%d", *r.ContainersRunning, *r.ContainersTotal)
		}
		disk := "-"
		if r.DiskPercent != nil {
			disk = fmt.Sprintf("%d%%", *r.DiskPercent)
		}
		load := "-"
		if r.Load1 != nil {
			load = fmt.Sprintf("%.2f", *r.Load1)
		}
		restart := ""
		if r.RebootRequired != nil && *r.RebootRequired {
			restart = "  restart"
		}
		fmt.Fprintf(w, "%-6d %-20s %-8s %-6s %-6s %s%s\n",
			r.Number, r.ReceivedAt.Format("2006-01-02 15:04"), containers, disk, load,
			Ago(now, r.ReceivedAt), restart)
	}
}

// Drift prints what the record and the machine disagree about: one sentence
// per disagreement, naming both sides and how old each is.
//
// **Which side is right, ha does not say.** That is the decision ADR 0015
// leaves to a person or an agent, and a CLI that picked one would be making it
// for them.
func Drift(w io.Writer, drift []api.Drift) {
	if len(drift) == 0 {
		return
	}

	now := time.Now()
	fmt.Fprintln(w)
	for _, one := range drift {
		record := or(one.Record)
		if one.RecordAt != nil {
			record += " (" + Ago(now, *one.RecordAt) + ")"
		}
		fmt.Fprintf(w, "drift  %s %s: the record says %s, the machine reported %s (%s)\n",
			one.Subject, one.Field, record, or(one.Reported), Ago(now, one.ReportedAt))
	}
}
