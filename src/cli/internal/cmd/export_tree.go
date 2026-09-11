package cmd

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// The tree looks like the repositories the product replaces (VISION 2, 14): a
// directory per machine, a directory per installation under it, the files at
// their paths, and the Markdown beside them. Nothing in it carries the moment
// it was written — two exports of the same record are the same bytes, which is
// what makes an export something to keep in a repository and diff.
func write(dir string, whole *record) error {
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("cannot write into %s: %v", dir, err)}
	}

	document, err := json.MarshalIndent(whole, "", "  ")
	if err != nil {
		return err
	}
	if err := put(filepath.Join(dir, "export.json"), string(document)+"\n"); err != nil {
		return err
	}

	if err := put(filepath.Join(dir, "README.md"), readme(whole)); err != nil {
		return err
	}

	for _, machine := range whole.Machines {
		at := filepath.Join(dir, "machines", machine.Key)

		if err := put(filepath.Join(at, "README.md"), machineMarkdown(machine)); err != nil {
			return err
		}
		if err := put(filepath.Join(at, "history.md"), historyMarkdown(machine.Key, machine.History)); err != nil {
			return err
		}
		if err := files(filepath.Join(at, "files"), machine.Files); err != nil {
			return err
		}

		for _, installation := range machine.Installations {
			under := filepath.Join(at, "installations", installation.Key)

			if err := put(filepath.Join(under, "README.md"), installationMarkdown(installation)); err != nil {
				return err
			}
			if err := put(filepath.Join(under, "history.md"), historyMarkdown(installation.Key, installation.History)); err != nil {
				return err
			}
			if err := put(filepath.Join(under, "deployments.md"), deploymentsMarkdown(installation)); err != nil {
				return err
			}
			if err := files(filepath.Join(under, "files"), installation.Files); err != nil {
				return err
			}
		}
	}

	for _, software := range whole.Software {
		if err := put(filepath.Join(dir, "software", software.Key+".md"), softwareMarkdown(software)); err != nil {
			return err
		}
	}

	for _, page := range whole.Pages {
		if err := put(filepath.Join(dir, "pages", page.Slug+".md"), pageMarkdown(page)); err != nil {
			return err
		}
	}

	return nil
}

// files writes an owner's files at their own paths, byte for byte: what is
// exported is what the machine runs.
func files(dir string, owned []api.File) error {
	for _, file := range owned {
		// The instance refuses a path that leaves its owner's directory, and
		// this is the second lock on the same door: an export writes to a
		// disk, and a client that trusts a path it was handed is one answer
		// away from writing outside the directory it was given.
		if !safePath(file.Path) {
			return &config.UsageError{
				Message: fmt.Sprintf("the instance answered with the path %q, which does not stay inside the export.", file.Path),
			}
		}
		if err := put(filepath.Join(dir, filepath.FromSlash(file.Path)), file.Content); err != nil {
			return err
		}
	}
	return nil
}

// safePath is the client's own check that a path stays where it was put: it is
// relative, has no `..` in it, and is not empty.
func safePath(path string) bool {
	if path == "" || strings.HasPrefix(path, "/") || strings.Contains(path, "\\") {
		return false
	}
	for _, segment := range strings.Split(path, "/") {
		if segment == "" || segment == "." || segment == ".." {
			return false
		}
	}
	return true
}

func put(path, content string) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("cannot write %s: %v", path, err)}
	}
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("cannot write %s: %v", path, err)}
	}
	return nil
}

func readme(whole *record) string {
	var out strings.Builder
	out.WriteString("# The record\n\nEverything this hostingaffe instance holds, written by `ha export`.\n\n")
	out.WriteString("- `export.json` is all of it, machine-readable, the history included. It is\n")
	out.WriteString("  the shape `ha machine add --file` reads, so an export can be imported again.\n")
	out.WriteString("- `machines/<key>/` is one machine: what it is, its history, its own files at\n")
	out.WriteString("  their paths, and a directory per installation on it.\n")
	out.WriteString("- `software/<key>.md` is what the installations are installations of.\n")
	out.WriteString("- `pages/<slug>.md` is the wiki, flat, as it is stored.\n\n")
	out.WriteString("Nothing here says when it was written: two exports of the same record are the\n")
	out.WriteString("same bytes, so this directory can be kept in a repository and diffed.\n\n")

	fmt.Fprintf(&out, "%d machines, %d installations, %d software, %d files, %d pages.\n",
		len(whole.Machines), whole.installations(), len(whole.Software), whole.files(), len(whole.Pages))

	if len(whole.Machines) > 0 {
		out.WriteString("\n| machine | kind | status | installations |\n|---|---|---|---|\n")
		for _, machine := range whole.Machines {
			fmt.Fprintf(&out, "| [%s](machines/%s/README.md) | %s | %s | %d |\n",
				machine.Key, machine.Key, machine.Kind, machine.Status, len(machine.Installations))
		}
	}

	return out.String()
}

func machineMarkdown(machine machineRecord) string {
	var out strings.Builder
	fmt.Fprintf(&out, "# %s — %s\n\n", machine.Key, machine.Name)
	fmt.Fprintf(&out, "A %s machine, %s.\n\n", machine.Kind, machine.Status)

	table(&out, [][2]string{
		{"hostname", value(machine.Hostname)},
		{"host", value(machine.Host)},
		{"provider", value(machine.Provider)},
		{"plan", value(machine.Plan)},
		{"location", value(machine.Location)},
		{"os", value(machine.Os)},
		{"arch", value((*string)(machine.Arch))},
		{"cpu", value(machine.Cpu)},
		{"memory", value(machine.Memory)},
		{"disk", value(machine.Disk)},
		{"ipv4", value(machine.Ipv4)},
		{"ipv6", value(machine.Ipv6)},
		{"private ip", value(machine.PrivateIp)},
		{"ssh", value(machine.Ssh)},
		{"measured", stamp(machine.MeasuredAt)},
	})

	body(&out, machine.Description)
	listing(&out, "Installations", links(machine.Installations, func(i installationRecord) (string, string) {
		return i.Key, fmt.Sprintf("installations/%s/README.md", i.Key)
	}))
	// A machine's file also says where on the machine it lies, and the export
	// is what somebody reads while rebuilding one: a unit without its directory
	// is a text nobody can put back (ADR 0008).
	listing(&out, "Files", links(machine.Files, func(f api.File) (string, string) {
		if f.Directory != nil && *f.Directory != "" {
			return fmt.Sprintf("%s → %s (revision %d)", f.Path, *f.Directory, f.Revision), "files/" + f.Path
		}
		return fmt.Sprintf("%s (revision %d)", f.Path, f.Revision), "files/" + f.Path
	}))

	return out.String()
}

func installationMarkdown(installation installationRecord) string {
	var out strings.Builder
	fmt.Fprintf(&out, "# %s — %s\n\n", installation.Key, installation.Name)
	fmt.Fprintf(&out, "%s on %s, %s, %s, %s.\n\n",
		installation.Software, installation.Machine,
		installation.Environment, installation.Role, installation.Status)

	table(&out, [][2]string{
		{"version", value(installation.Version)},
		{"backup", string(installation.Backup)},
		{"monitoring", string(installation.Monitoring)},
		{"logging", string(installation.Logging)},
		{"path", value(installation.Path)},
		{"ports", render.Ports(installation.Ports)},
		{"urls", strings.Join(installation.Urls, ", ")},
		{"secrets", strings.Join(installation.Secrets, ", ")},
	})

	body(&out, installation.Description)
	listing(&out, "Files", links(installation.Files, func(f api.File) (string, string) {
		return fmt.Sprintf("%s (revision %d)", f.Path, f.Revision), "files/" + f.Path
	}))

	if len(installation.Deployments) > 0 {
		out.WriteString("\nWhat ran here, and when: [deployments.md](deployments.md).\n")
	}

	return out.String()
}

func softwareMarkdown(software softwareRecord) string {
	var out strings.Builder
	fmt.Fprintf(&out, "# %s — %s\n\n", software.Key, software.Name)
	out.WriteString("It carries no version: versions belong to deployments.\n\n")

	table(&out, [][2]string{
		{"image", value(software.Image)},
		{"homepage", value(software.Homepage)},
		{"repository", value(software.Repository)},
	})

	body(&out, software.Description)
	history(&out, software.History)
	return out.String()
}

func pageMarkdown(page pageRecord) string {
	var out strings.Builder
	fmt.Fprintf(&out, "# %s\n\n", page.Title)
	fmt.Fprintf(&out, "`%s`, a %s, hanging on %s.\n\n", page.Slug, page.Kind, render.Anchor(page.AttachedTo))

	if page.Body != "" {
		out.WriteString("---\n\n")
		out.WriteString(strings.TrimRight(page.Body, "\n"))
		out.WriteString("\n")
	}

	history(&out, page.History)
	return out.String()
}

// deploymentsMarkdown is the answer to the question a git log was the answer to
// before (VISION 2): what changed here, and when, newest first.
func deploymentsMarkdown(installation installationRecord) string {
	var out strings.Builder
	fmt.Fprintf(&out, "# Deployments of %s\n\nNewest first, in the order everything derived uses: by `at`.\n", installation.Key)

	// The JSON keeps them in the order the instance numbered them; this reads
	// them in the order everything derived is computed in, newest by `at`
	// first, so a backfilled deployment sits where it belongs and not at the
	// top because it was recorded last.
	ordered := sorted(installation.Deployments, func(d api.Deployment) string {
		return fmt.Sprintf("%s-%09d", d.At.UTC().Format(time.RFC3339Nano), d.Number)
	})

	for at := len(ordered) - 1; at >= 0; at-- {
		deployment := ordered[at]

		fmt.Fprintf(&out, "\n## %d — %s\n\n", deployment.Number, deployment.Version)
		table(&out, [][2]string{
			{"at", deployment.At.Format(time.RFC3339)},
			{"by", deployment.By.Name},
			{"previous", value(deployment.Previous)},
			{"ticket", value(deployment.Ticket)},
			{"ref", value(deployment.Ref)},
			{"files", strings.Join(deployed(deployment.Files), ", ")},
		})
		body(&out, deployment.Note)
	}

	return out.String()
}

func deployed(files []api.DeploymentFile) []string {
	spelled := make([]string, 0, len(files))
	for _, file := range files {
		spelled = append(spelled, fmt.Sprintf("%s@%d", file.Path, file.Revision))
	}
	return spelled
}

// historyMarkdown is what a `git log` was: who changed what, oldest first, with
// the note that came with the change.
func historyMarkdown(key string, entries []api.HistoryEntry) string {
	var out strings.Builder
	fmt.Fprintf(&out, "# History of %s\n\nOldest first. A text records that it changed, not how.\n\n", key)

	if len(entries) == 0 {
		out.WriteString("Nothing recorded.\n")
		return out.String()
	}

	out.WriteString("| when | who | field | from | to | note |\n|---|---|---|---|---|---|\n")
	for _, entry := range entries {
		fmt.Fprintf(&out, "| %s | %s | %s | %s | %s | %s |\n",
			entry.At.Format(time.RFC3339), entry.Actor.Name, entry.Field,
			value(entry.OldValue), value(entry.NewValue), value(entry.Note))
	}
	return out.String()
}

func history(out *strings.Builder, entries []api.HistoryEntry) {
	if len(entries) == 0 {
		return
	}
	out.WriteString("\n## History\n\n")
	out.WriteString("| when | who | field | from | to | note |\n|---|---|---|---|---|---|\n")
	for _, entry := range entries {
		fmt.Fprintf(out, "| %s | %s | %s | %s | %s | %s |\n",
			entry.At.Format(time.RFC3339), entry.Actor.Name, entry.Field,
			value(entry.OldValue), value(entry.NewValue), value(entry.Note))
	}
}

// table prints the rows that have a value and nothing at all where none does.
func table(out *strings.Builder, rows [][2]string) {
	said := make([][2]string, 0, len(rows))
	for _, row := range rows {
		if row[1] != "" && row[1] != "—" {
			said = append(said, row)
		}
	}
	if len(said) == 0 {
		return
	}

	out.WriteString("| | |\n|---|---|\n")
	for _, row := range said {
		fmt.Fprintf(out, "| %s | %s |\n", row[0], row[1])
	}
}

func listing(out *strings.Builder, title string, entries []string) {
	if len(entries) == 0 {
		return
	}
	fmt.Fprintf(out, "\n## %s\n\n", title)
	for _, entry := range entries {
		fmt.Fprintf(out, "- %s\n", entry)
	}
}

func links[T any](items []T, of func(T) (string, string)) []string {
	entries := make([]string, 0, len(items))
	for _, item := range items {
		text, target := of(item)
		entries = append(entries, fmt.Sprintf("[%s](%s)", text, target))
	}
	return entries
}

func body(out *strings.Builder, text string) {
	if strings.TrimSpace(text) != "" {
		fmt.Fprintf(out, "\n%s\n", strings.TrimRight(text, "\n"))
	}
}

// value is what a table cell says where there is nothing: a dash, which is not
// the same as an empty cell nobody filled in.
func value(text *string) string {
	if text == nil || *text == "" {
		return ""
	}
	return *text
}

func stamp(at *time.Time) string {
	if at == nil {
		return ""
	}
	return at.Format(time.RFC3339)
}
