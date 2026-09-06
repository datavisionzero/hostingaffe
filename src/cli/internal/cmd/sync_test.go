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

// syncing answers an installation's files: whatever the test says it has.
func syncing(files map[string]string) *fake {
	return &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if strings.HasSuffix(r.URL.Path, "/files") {
			summaries := make([]string, 0, len(files))
			for path := range files {
				summaries = append(summaries, `{"owner":{"kind":"installation","key":"logaffe-prod"},"path":`+
					quoted(path)+`,"executable":false,"revision":1,
					"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
					"updated_at":"2026-09-05T12:00:00Z"}`)
			}
			return 200, "[" + strings.Join(summaries, ",") + "]"
		}

		path := strings.TrimPrefix(r.URL.Path, "/api/installations/logaffe-prod/files/")
		content, there := files[path]
		if !there {
			return 404, `{"type":"/problems/not-found","title":"not-found","status":404,"detail":"No file."}`
		}
		return 200, `{"owner":{"kind":"installation","key":"logaffe-prod"},"path":` + quoted(path) + `,
		"executable":false,"content":` + quoted(content) + `,"revision":1,
		"created_by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
		"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
		"created_at":"2026-09-05T10:00:00Z","updated_at":"2026-09-05T12:00:00Z"}`
	}}
}

func quoted(text string) string {
	out, _ := json.Marshal(text)
	return string(out)
}

// The files land at their places, and a second run has nothing to do.
func TestSyncWritesTheFilesAndThenHasNothingToDo(t *testing.T) {
	f := syncing(map[string]string{
		"compose.yml":     "services:\n  app:\n    image: app:1\n",
		"sites/app.caddy": "app.example.test {\n  reverse_proxy app:8080\n}\n",
	})
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := filepath.Join(t.TempDir(), "srv")

	code, out, stderr := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if out != "written    compose.yml\nwritten    sites/app.caddy\n" {
		t.Fatalf("what it did:\n%q", out)
	}
	if got := read(t, filepath.Join(dir, "sites/app.caddy")); got != "app.example.test {\n  reverse_proxy app:8080\n}\n" {
		t.Errorf("the file, byte for byte:\n%q", got)
	}

	// The manifest is beside the files, and it is the only state outside the
	// instance.
	if _, err := os.Stat(filepath.Join(dir, Manifest)); err != nil {
		t.Fatalf("no manifest: %v", err)
	}

	code, out, _ = run(t, server, "files", "sync", dir, "--inst", "logaffe-prod")
	if code != exit.OK {
		t.Fatalf("code %d", code)
	}
	if out != "unchanged  compose.yml\nunchanged  sites/app.caddy\n" {
		t.Fatalf("a second run changes nothing:\n%q", out)
	}
}

// What sync wrote, sync clears away once the record no longer has it — and the
// directory it emptied goes with it.
func TestWhatLeavesTheRecordLeavesTheDirectory(t *testing.T) {
	files := map[string]string{"compose.yml": "one\n", "sites/app.caddy": "two\n"}
	f := syncing(files)
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := filepath.Join(t.TempDir(), "srv")
	if code, _, _ := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod"); code != exit.OK {
		t.Fatal("code")
	}

	delete(files, "sites/app.caddy")

	code, out, stderr := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if out != "unchanged  compose.yml\nremoved    sites/app.caddy\n" {
		t.Fatalf("what it did:\n%q", out)
	}
	if _, err := os.Stat(filepath.Join(dir, "sites/app.caddy")); err == nil {
		t.Error("what left the record is still there")
	}
	if _, err := os.Stat(filepath.Join(dir, "sites")); err == nil {
		t.Error("the directory a removal emptied is still there")
	}
	if _, err := os.Stat(dir); err != nil {
		t.Error("the directory sync was given is not sync's to remove")
	}
}

// What sync never wrote, sync never touches — even when it is exactly in the
// way of a file the record has.
func TestWhatSyncNeverWroteIsNeverTouched(t *testing.T) {
	f := syncing(map[string]string{"compose.yml": "from the record\n", "other.yml": "also\n"})
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "compose.yml"), []byte("somebody's own\n"), 0o644); err != nil {
		t.Fatal(err)
	}

	code, out, stderr := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod")

	// A directory that is not what the record says is a conflict, and a script
	// has to be able to tell.
	if code != exit.Conflict || stderr == "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if !strings.Contains(out, "in the way compose.yml  (sync never wrote it)") {
		t.Fatalf("what it did:\n%s", out)
	}
	if got := read(t, filepath.Join(dir, "compose.yml")); got != "somebody's own\n" {
		t.Errorf("it was touched: %q", got)
	}

	// And everything else was still written: what sync could do, it did.
	if got := read(t, filepath.Join(dir, "other.yml")); got != "also\n" {
		t.Errorf("other.yml = %q", got)
	}

	// It never becomes sync's by being in the way twice.
	if code, _, _ = run(t, server, "files", "sync", dir, "--inst", "logaffe-prod"); code != exit.Conflict {
		t.Fatalf("code %d", code)
	}
	if got := read(t, filepath.Join(dir, "compose.yml")); got != "somebody's own\n" {
		t.Errorf("it was touched: %q", got)
	}
}

// A file sync wrote and somebody changed on the host: the record is what the
// machine runs, and sync says out loud that it put it back.
func TestAFileChangedOnTheHostIsPutBack(t *testing.T) {
	files := map[string]string{"compose.yml": "from the record\n"}
	f := syncing(files)
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := t.TempDir()
	if code, _, _ := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod"); code != exit.OK {
		t.Fatal("code")
	}
	if err := os.WriteFile(filepath.Join(dir, "compose.yml"), []byte("edited in a hurry\n"), 0o644); err != nil {
		t.Fatal(err)
	}

	code, out, _ := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod")
	if code != exit.OK {
		t.Fatalf("code %d", code)
	}
	if out != "restored   compose.yml  (it had been changed on the host)\n" {
		t.Fatalf("what it did:\n%q", out)
	}
	if got := read(t, filepath.Join(dir, "compose.yml")); got != "from the record\n" {
		t.Errorf("compose.yml = %q", got)
	}
}

// One that sync wrote, that was changed on the host, and that has since left
// the record: it stops being sync's rather than being thrown away.
func TestOneChangedOnTheHostAndGoneFromTheRecordIsKept(t *testing.T) {
	files := map[string]string{"compose.yml": "one\n"}
	f := syncing(files)
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := t.TempDir()
	if code, _, _ := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod"); code != exit.OK {
		t.Fatal("code")
	}
	if err := os.WriteFile(filepath.Join(dir, "compose.yml"), []byte("edited\n"), 0o644); err != nil {
		t.Fatal(err)
	}
	delete(files, "compose.yml")

	code, out, _ := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod")
	if code != exit.OK {
		t.Fatalf("code %d", code)
	}
	if out != "kept       compose.yml  (changed on the host since sync wrote it)\n" {
		t.Fatalf("what it did:\n%q", out)
	}
	if got := read(t, filepath.Join(dir, "compose.yml")); got != "edited\n" {
		t.Errorf("compose.yml = %q", got)
	}

	// And the manifest has forgotten it: it is not sync's any more.
	held := manifest{}
	if err := json.Unmarshal([]byte(read(t, filepath.Join(dir, Manifest))), &held); err != nil {
		t.Fatal(err)
	}
	if _, still := held.Files["compose.yml"]; still {
		t.Error("the manifest still claims it")
	}
}

// `--dry-run` prints what a run would do and touches nothing.
func TestDryRunSaysTheSameAndDoesNothing(t *testing.T) {
	f := syncing(map[string]string{"compose.yml": "one\n"})
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := t.TempDir()

	code, dry, stderr := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod", "--dry-run")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if entries, _ := os.ReadDir(dir); len(entries) != 0 {
		t.Fatalf("it wrote something: %v", entries)
	}

	code, wet, _ := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod")
	if code != exit.OK {
		t.Fatal("code")
	}

	if strings.TrimSuffix(dry, "nothing was written: --dry-run.\n") != wet {
		t.Fatalf("a dry run says what a run does:\ndry: %q\nrun: %q", dry, wet)
	}
}

// A directory holding one owner's files is not another owner's to sync into.
func TestADirectoryHoldsOneOwnersFiles(t *testing.T) {
	f := syncing(map[string]string{"compose.yml": "one\n"})
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := t.TempDir()
	if code, _, _ := run(t, server, "files", "sync", dir, "--inst", "logaffe-prod"); code != exit.OK {
		t.Fatal("code")
	}

	if code, _, stderr := run(t, server, "files", "sync", dir, "--machine", "ex44"); code != exit.Usage || stderr == "" {
		t.Errorf("code %d, stderr %q", code, stderr)
	}
}

func TestSyncUsageMistakesAreExitTwo(t *testing.T) {
	f := syncing(map[string]string{})
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, args := range [][]string{
		{"files", "sync", t.TempDir()},
		{"files", "sync", "--inst", "logaffe-prod"},
	} {
		if code, _, stderr := run(t, server, args...); code != exit.Usage || stderr == "" {
			t.Errorf("%v: code %d, stderr %q", args, code, stderr)
		}
	}
}
