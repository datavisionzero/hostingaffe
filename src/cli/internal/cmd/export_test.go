package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

// A whole small record, answered from one server: one machine with one
// installation, one file under each, a deployment, a software and a page.
func exported() *fake {
	machineFile := strings.ReplaceAll(fileJSON, `"kind":"installation","key":"logaffe-prod"`, `"kind":"machine","key":"ex44"`)
	machineFile = strings.ReplaceAll(machineFile, `"path":"compose.override.yml"`, `"path":"sites/logaffe.caddy"`)
	machineFile = strings.ReplaceAll(machineFile,
		`"content":"services:\n  logaffe:\n    image: logaffe:1.4.0\n"`,
		`"content":"logs.example.test {\n  reverse_proxy logaffe:8080\n}\n"`)
	machineFile = strings.ReplaceAll(machineFile, `"revision":3`, `"revision":1`)

	return &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		path := r.URL.Path
		switch {
		case path == "/api/machines":
			return 200, `[{"key":"ex44","name":"The big one","kind":"dedicated","status":"active","provider":"hetzner",
			"location":"fsn1-dc14","arch":"amd64","measured_at":null,"updated_at":"2026-09-05T12:00:00Z"}]`
		case path == "/api/installations":
			return 200, `[{"key":"logaffe-prod","name":"Logaffe production","machine":"ex44","software":"logaffe",
			"environment":"production","role":"application","status":"active","backup":"active","monitoring":"external",
			"logging":"central","version":"1.4.0","updated_at":"2026-09-05T12:00:00Z"}]`
		case path == "/api/software":
			return 200, `[{"key":"logaffe","name":"logaffe","homepage":null,
			"image":"ghcr.io/datavisionzero/logaffe","updated_at":"2026-09-05T12:00:00Z"}]`
		case path == "/api/pages":
			return 200, `[{"slug":"architecture","title":"Architecture","kind":"decision","attached_to":null,
			"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
			"created_at":"2026-09-05T10:00:00Z","updated_at":"2026-09-05T12:00:00Z"}]`

		case strings.HasSuffix(path, "/history"):
			return 200, historyJSON
		case strings.HasSuffix(path, "/deployments"):
			return 200, deploymentsJSON
		case strings.Contains(path, "/deployments/"):
			return 200, deploymentJSON

		case strings.HasSuffix(path, "/files"):
			if strings.HasPrefix(path, "/api/machines/") {
				return 200, `[{"owner":{"kind":"machine","key":"ex44"},"path":"sites/logaffe.caddy",
				"executable":false,"revision":1,
				"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
				"updated_at":"2026-09-05T12:00:00Z"}]`
			}
			return 200, filesJSON
		case strings.Contains(path, "/files/"):
			if strings.HasPrefix(path, "/api/machines/") {
				return 200, machineFile
			}
			return 200, fileJSON

		case path == "/api/machines/ex44":
			return 200, machineJSON
		case path == "/api/software/logaffe":
			return 200, softwareJSON
		case path == "/api/installations/logaffe-prod":
			return 200, installationJSON
		default:
			return 200, page
		}
	}}
}

// The tree looks like the repositories it replaces, and the files are at their
// own paths with their own content.
func TestAnExportIsATreeWithTheFilesInPlace(t *testing.T) {
	f := exported()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := filepath.Join(t.TempDir(), "hosting-export")
	code, out, stderr := run(t, server, "export", "--dir", dir)
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if !strings.Contains(out, "1 machines, 1 installations") {
		t.Errorf("it says what it wrote: %q", out)
	}

	for _, want := range []string{
		"README.md",
		"export.json",
		"machines/ex44/README.md",
		"machines/ex44/history.md",
		"machines/ex44/files/sites/logaffe.caddy",
		"machines/ex44/installations/logaffe-prod/README.md",
		"machines/ex44/installations/logaffe-prod/history.md",
		"machines/ex44/installations/logaffe-prod/deployments.md",
		"machines/ex44/installations/logaffe-prod/files/compose.override.yml",
		"software/logaffe.md",
		"pages/architecture.md",
	} {
		if _, err := os.Stat(filepath.Join(dir, want)); err != nil {
			t.Errorf("%s is missing", want)
		}
	}

	// A file is what the machine runs, byte for byte, at its own path.
	content := read(t, filepath.Join(dir, "machines/ex44/files/sites/logaffe.caddy"))
	if content != "logs.example.test {\n  reverse_proxy logaffe:8080\n}\n" {
		t.Errorf("the file, byte for byte:\n%q", content)
	}

	// The Markdown says what the record says.
	machine := read(t, filepath.Join(dir, "machines/ex44/README.md"))
	if !strings.HasPrefix(machine, "# ex44 — The big one\n") {
		t.Errorf("the machine's head:\n%s", machine)
	}
	if !strings.Contains(machine, "| disk | 2×512G NVMe ZFS mirror |") {
		t.Errorf("the fields:\n%s", machine)
	}
	if !strings.Contains(machine, "[sites/logaffe.caddy (revision 1)](files/sites/logaffe.caddy)") {
		t.Errorf("the files are linked where they are:\n%s", machine)
	}

	installation := read(t, filepath.Join(dir, "machines/ex44/installations/logaffe-prod/README.md"))
	if !strings.Contains(installation, "| ports | 443/tcp:public, 5432/tcp:private |") {
		t.Errorf("the ports as a person reads them:\n%s", installation)
	}
}

// The history is in the JSON, which is what the ticket asks for, and in the
// tree, which is what a `git log` used to be.
func TestAnExportCarriesTheHistory(t *testing.T) {
	f := exported()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := filepath.Join(t.TempDir(), "hosting-export")
	if code, _, stderr := run(t, server, "export", "--dir", dir); code != exit.OK {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	document := map[string]any{}
	if err := json.Unmarshal([]byte(read(t, filepath.Join(dir, "export.json"))), &document); err != nil {
		t.Fatalf("export.json is not JSON: %v", err)
	}

	machines, _ := document["machines"].([]any)
	if len(machines) != 1 {
		t.Fatalf("machines = %v", document["machines"])
	}
	machine, _ := machines[0].(map[string]any)

	for _, field := range []string{"key", "kind", "description", "installations", "files", "history"} {
		if _, ok := machine[field]; !ok {
			t.Errorf("the machine has no %s: %v", field, machine)
		}
	}
	if history, _ := machine["history"].([]any); len(history) != 2 {
		t.Errorf("history = %v", machine["history"])
	}

	installations, _ := machine["installations"].([]any)
	installation, _ := installations[0].(map[string]any)
	for _, field := range []string{"deployments", "files", "history", "ports", "version"} {
		if _, ok := installation[field]; !ok {
			t.Errorf("the installation has no %s: %v", field, installation)
		}
	}

	// And the tree says it too, with the note that came with the change.
	if got := read(t, filepath.Join(dir, "machines/ex44/history.md")); !strings.Contains(got, "dist-upgrade") {
		t.Errorf("the history in the tree:\n%s", got)
	}
}

// Two exports of the same record are the same bytes: nothing carries the moment
// it was written, so an export can be kept in a repository and diffed.
func TestTwoExportsOfOneRecordAreTheSameBytes(t *testing.T) {
	f := exported()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	root := t.TempDir()
	first, second := filepath.Join(root, "one"), filepath.Join(root, "two")

	for _, dir := range []string{first, second} {
		if code, _, stderr := run(t, server, "export", "--dir", dir); code != exit.OK {
			t.Fatalf("code %d, stderr %q", code, stderr)
		}
	}

	written := map[string]string{}
	for _, dir := range []string{first, second} {
		_ = filepath.WalkDir(dir, func(path string, entry os.DirEntry, err error) error {
			if err != nil || entry.IsDir() {
				return err
			}
			at := strings.TrimPrefix(path, dir)
			content := read(t, path)
			if dir == first {
				written[at] = content
			} else if written[at] != content {
				t.Errorf("%s differs between two exports", at)
			}
			return nil
		})
	}
	if len(written) == 0 {
		t.Fatal("nothing was written")
	}
}

// An export replaces an export, and leaves alone a directory it did not write.
func TestAnExportOnlyReplacesAnExport(t *testing.T) {
	f := exported()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := filepath.Join(t.TempDir(), "hosting-export")
	if code, _, _ := run(t, server, "export", "--dir", dir); code != exit.OK {
		t.Fatal("code")
	}

	// A file from an older export that this record no longer has is gone.
	stale := filepath.Join(dir, "machines", "gone", "README.md")
	if err := os.MkdirAll(filepath.Dir(stale), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(stale, []byte("x"), 0o644); err != nil {
		t.Fatal(err)
	}

	if code, _, stderr := run(t, server, "export", "--dir", dir); code != exit.OK {
		t.Fatalf("an export replaces an export: code %d, stderr %q", code, stderr)
	}
	if _, err := os.Stat(stale); err == nil {
		t.Error("what the record no longer has is not left behind")
	}

	// Anything else is left alone, and said so before a request goes out.
	somebodys := t.TempDir()
	if err := os.WriteFile(filepath.Join(somebodys, "notes.txt"), []byte("mine"), 0o644); err != nil {
		t.Fatal(err)
	}
	before := len(f.requests)
	if code, _, stderr := run(t, server, "export", "--dir", somebodys); code != exit.Usage || stderr == "" {
		t.Errorf("code %d, stderr %q", code, stderr)
	}
	if len(f.requests) != before {
		t.Error("nothing is read before it is known where it goes")
	}
	if _, err := os.Stat(filepath.Join(somebodys, "notes.txt")); err != nil {
		t.Error("ha does not remove what it did not write")
	}
}

func TestExportWithoutADirIsExitTwo(t *testing.T) {
	f := exported()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	if code, _, stderr := run(t, server, "export"); code != exit.Usage || stderr == "" {
		t.Errorf("code %d, stderr %q", code, stderr)
	}
}

func read(t *testing.T, path string) string {
	t.Helper()
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("cannot read %s: %v", path, err)
	}
	return string(content)
}
