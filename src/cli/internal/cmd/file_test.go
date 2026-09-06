package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const (
	fileJSON = `{"owner":{"kind":"installation","key":"logaffe-prod"},"path":"compose.override.yml",
"executable":false,"content":"services:\n  logaffe:\n    image: logaffe:1.4.0\n","revision":3,
"created_by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
"created_at":"2026-09-05T10:00:00.000000Z","updated_at":"2026-09-05T12:00:00.000000Z"}`

	filesJSON = `[{"owner":{"kind":"installation","key":"logaffe-prod"},"path":"compose.override.yml",
"executable":false,"revision":3,
"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
"updated_at":"2026-09-05T12:00:00Z"}]`

	revisionsJSON = `[{"revision":3,"executable":false,
"by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
"at":"2026-09-05T12:00:00Z"},{"revision":2,"executable":false,
"by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"at":"2026-09-05T11:00:00Z"}]`
)

func fileServer() *fake {
	return &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.Method == http.MethodDelete:
			return 204, ""
		case strings.Contains(r.URL.Path, "/file-history/"):
			return 200, historyJSON
		case strings.Contains(r.URL.Path, "/file-revisions/"):
			return 200, revisionsJSON
		case r.Method == http.MethodPost && strings.HasSuffix(r.URL.Path, "/files"):
			return 201, fileJSON
		case r.Method == http.MethodGet && strings.HasSuffix(r.URL.Path, "/files"):
			return 200, filesJSON
		default:
			return 200, fileJSON
		}
	}}
}

// Both owners get the same verbs under a different prefix, and the kind belongs
// to the owner because the machine `caddy` and the software `caddy` are
// different things.
func TestFileVerbsReachBothOwners(t *testing.T) {
	f := fileServer()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	const path = "compose.override.yml"

	for _, tc := range []struct {
		args         []string
		method, want string
	}{
		{[]string{"files", "list", "--installation", "logaffe-prod"}, "GET", "/api/installations/logaffe-prod/files"},
		{[]string{"files", "list", "--machine", "ex44"}, "GET", "/api/machines/ex44/files"},
		{[]string{"files", "get", path, "--inst", "logaffe-prod"}, "GET", "/api/installations/logaffe-prod/files/" + path},
		{[]string{"files", "get", path, "--machine", "ex44"}, "GET", "/api/machines/ex44/files/" + path},
		{[]string{"files", "put", path, "--inst", "logaffe-prod", "--file", "-"}, "PUT", "/api/installations/logaffe-prod/files/" + path},
		{[]string{"files", "revisions", path, "--inst", "logaffe-prod"}, "GET", "/api/installations/logaffe-prod/file-revisions/" + path},
		{[]string{"files", "history", path, "--machine", "ex44"}, "GET", "/api/machines/ex44/file-history/" + path},
		{[]string{"files", "delete", path, "--inst", "logaffe-prod"}, "DELETE", "/api/installations/logaffe-prod/files/" + path},
		{[]string{"files", "restore", path, "--machine", "ex44"}, "POST", "/api/machines/ex44/file-restore/" + path},
	} {
		code, _, stderr := run(t, server, tc.args...)
		if code != exit.OK || stderr != "" {
			t.Fatalf("%v: code %d, stderr %q", tc.args, code, stderr)
		}
		last := f.requests[len(f.requests)-1]
		if last.Method != tc.method || last.URL.Path != tc.want {
			t.Errorf("%v: %s %s", tc.args, last.Method, last.URL.Path)
		}
	}
}

// `get` is the content and nothing else, so that it can be redirected into a
// file; `--json` is the record, with the revision a write hands back.
func TestGetIsTheContentAndJsonIsTheRecord(t *testing.T) {
	f := fileServer()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "files", "get", "compose.override.yml", "--inst", "logaffe-prod")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if out != "services:\n  logaffe:\n    image: logaffe:1.4.0\n" {
		t.Fatalf("the content, byte for byte, as it is stored:\n%q", out)
	}

	code, out, _ = run(t, server, "--json", "files", "get", "compose.override.yml", "--inst", "logaffe-prod")
	if code != exit.OK {
		t.Fatal("code")
	}
	record := map[string]any{}
	if err := json.Unmarshal([]byte(out), &record); err != nil {
		t.Fatalf("stdout is not JSON: %v", err)
	}
	if record["revision"] != float64(3) {
		t.Errorf("the record carries the revision a write hands back: %v", record)
	}

	// An earlier revision is a read parameter, as the endpoint offers it.
	if code, _, _ = run(t, server, "files", "get", "compose.override.yml", "--inst", "logaffe-prod", "--revision", "2"); code != exit.OK {
		t.Fatal("code")
	}
	if got := f.requests[len(f.requests)-1].URL.Query().Get("revision"); got != "2" {
		t.Errorf("revision = %q", got)
	}
}

// The revision is the write guard: what `get --json` printed is what `put`
// hands back, and it travels as the file's entity tag.
func TestTheRevisionIsTheWriteGuard(t *testing.T) {
	f := fileServer()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "files", "put", "compose.override.yml",
		"--inst", "logaffe-prod", "--file", "-", "--revision", "2", "--note", "raised the memory limit")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	last := f.requests[len(f.requests)-1]
	if got := last.Header.Get("If-Match"); got != `"2"` {
		t.Errorf("If-Match = %q", got)
	}
	if got := last.URL.Query().Get("note"); got != "raised the memory limit" {
		t.Errorf("note = %q", got)
	}

	// Every write prints the revision it produced.
	if !strings.HasPrefix(out, "compose.override.yml  revision 3  installation logaffe-prod\n") {
		t.Fatalf("a write says the revision it made:\n%s", out)
	}

	// Without it, the write wins and no guard is sent.
	if code, _, _ = run(t, server, "files", "put", "compose.override.yml", "--inst", "logaffe-prod", "--file", "-"); code != exit.OK {
		t.Fatal("code")
	}
	if f.requests[len(f.requests)-1].Header.Get("If-Match") != "" {
		t.Error("no --revision, no guard")
	}
}

// A write against a newer revision is exit 6, as docs/cli.md lays it down.
func TestAWriteAgainstAnOlderRevisionIsExitSix(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 412, `{"type":"/problems/stale","title":"stale","status":412,
		"detail":"compose.override.yml is at revision 3; you last read revision 2."}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "files", "put", "compose.override.yml",
		"--inst", "logaffe-prod", "--file", "-", "--revision", "2")
	if code != exit.Stale {
		t.Fatalf("code %d", code)
	}
	if out != "" || !strings.Contains(stderr, "revision 3") {
		t.Fatalf("stdout %q, stderr %q", out, stderr)
	}
}

// The first write of a file is a create, and `put` does not make the caller
// know which it is.
func TestPutCreatesWhatIsNotThereYet(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method == http.MethodPut {
			return 404, `{"type":"/problems/not-found","title":"not-found","status":404,
			"detail":"No file compose.override.yml of installation logaffe-prod."}`
		}
		return 201, fileJSON
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := run(t, server, "files", "put", "compose.override.yml", "--inst", "logaffe-prod", "--file", "-")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	if len(f.requests) != 2 || f.requests[0].Method != http.MethodPut || f.requests[1].Method != http.MethodPost {
		t.Fatalf("it writes first and creates on a not-found: %d requests", len(f.requests))
	}
	created := map[string]any{}
	_ = json.Unmarshal([]byte(f.bodies[1]), &created)
	if created["path"] != "compose.override.yml" {
		t.Errorf("a create carries the path: %v", created)
	}

	// With a revision, a not-found is a not-found: nobody read a revision of a
	// file that is not there.
	if code, _, _ = run(t, server, "files", "put", "compose.override.yml",
		"--inst", "logaffe-prod", "--file", "-", "--revision", "2"); code != exit.NotFound {
		t.Fatalf("code %d", code)
	}
}

// A refused path is the instance's to refuse, and ha passes it through as exit
// 4 rather than keeping a second copy of the list.
func TestARefusedPathIsExitFour(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 400, `{"type":"/problems/validation","title":"validation","status":400,
		"detail":"A file at .env would carry secrets; .env.example is welcome.","errors":{"path":["Refused."]}}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "files", "put", ".env", "--inst", "logaffe-prod", "--file", "-")
	if code != exit.Refused {
		t.Fatalf("code %d", code)
	}
	if out != "" || !strings.Contains(stderr, ".env.example is welcome") {
		t.Fatalf("stdout %q, stderr %q", out, stderr)
	}
}

// Two revisions, side by side. Without either end it is the last change, which
// is the question somebody usually has.
func TestDiffComparesTwoRevisions(t *testing.T) {
	older := strings.ReplaceAll(fileJSON, `"revision":3`, `"revision":2`)
	older = strings.ReplaceAll(older, "image: logaffe:1.4.0", "image: logaffe:1.3.2")

	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Query().Get("revision") == "2" {
			return 200, older
		}
		return 200, fileJSON
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "files", "diff", "compose.override.yml", "--inst", "logaffe-prod")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	want := "--- compose.override.yml@2\n+++ compose.override.yml@3\n" +
		"@@ -1,3 +1,3 @@\n services:\n   logaffe:\n" +
		"-    image: logaffe:1.3.2\n+    image: logaffe:1.4.0\n"
	if out != want {
		t.Fatalf("diff:\n%q\nwant:\n%q", out, want)
	}

	// The two ends are read, in the order the defaults need them.
	if got := f.requests[0].URL.Query().Has("revision"); got {
		t.Error("without --to, the newer end is the file as it is")
	}
	if got := f.requests[1].URL.Query().Get("revision"); got != "2" {
		t.Errorf("without --from, the older end is the one before: %q", got)
	}
}

func TestFileUsageMistakesAreExitTwo(t *testing.T) {
	f := fileServer()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, args := range [][]string{
		{"files", "list"},
		{"files", "list", "--machine", "ex44", "--installation", "logaffe-prod"},
		{"files", "get", "compose.override.yml"},
		{"files", "put", "compose.override.yml", "--inst", "logaffe-prod"},
	} {
		if code, _, stderr := run(t, server, args...); code != exit.Usage || stderr == "" {
			t.Errorf("%v: code %d, stderr %q", args, code, stderr)
		}
	}
}

// A file with one revision has nothing to compare itself with, and that is said
// before a second read goes out.
func TestDiffOfAFirstRevisionSaysThereIsNothingToCompare(t *testing.T) {
	first := strings.ReplaceAll(fileJSON, `"revision":3`, `"revision":1`)
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, first }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := run(t, server, "files", "diff", "compose.override.yml", "--inst", "logaffe-prod")
	if code != exit.Usage || !strings.Contains(stderr, "one revision") {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if len(f.requests) != 1 {
		t.Errorf("nothing to compare, nothing more to read: %d requests", len(f.requests))
	}
}
