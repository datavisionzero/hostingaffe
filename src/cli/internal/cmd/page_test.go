package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const page = `{"slug":"architecture","project":"PLAN","title":"Architecture","body":"# The four layers\n\nDependencies point inward.",
"author":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
"created_at":"2026-09-05T10:00:00.000000Z","updated_at":"2026-09-05T12:00:00.000000Z"}`

func TestPageVerbsReachTheRightAddresses(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method == http.MethodGet && r.URL.Path == "/projects/PLAN/pages" {
			return 200, `[{"slug":"architecture","project":"PLAN","title":"Architecture",
			"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
			"created_at":"2026-09-05T10:00:00Z","updated_at":"2026-09-05T12:00:00Z"}]`
		}
		if r.Method == http.MethodPost && r.URL.Path == "/projects/PLAN/pages" {
			return 201, page
		}
		if r.Method == http.MethodDelete {
			return 204, ""
		}
		return 200, page
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := repository(t, "project = PLAN\n")

	for _, tc := range []struct {
		args                   []string
		method, path, contains string
	}{
		{[]string{"page", "list"}, "GET", "/projects/PLAN/pages", "architecture"},
		{[]string{"page", "view", "architecture"}, "GET", "/projects/PLAN/pages/architecture", "# The four layers"},
		{[]string{"page", "create", "architecture", "--title", "Architecture"}, "POST", "/projects/PLAN/pages", "PLAN/architecture"},
		{[]string{"page", "edit", "architecture", "--title", "The four layers"}, "PATCH", "/projects/PLAN/pages/architecture", "PLAN/architecture"},
		{[]string{"page", "rename", "architecture", "betriebshandbuch"}, "PATCH", "/projects/PLAN/pages/architecture", "PLAN/architecture"},
		{[]string{"page", "delete", "architecture"}, "DELETE", "/projects/PLAN/pages/architecture", "ha page restore architecture"},
		{[]string{"page", "restore", "architecture"}, "POST", "/projects/PLAN/pages/architecture/restore", "PLAN/architecture"},
	} {
		code, out, stderr := run(t, server, dir, tc.args...)
		if code != exit.OK || stderr != "" {
			t.Fatalf("%v: code %d, stderr %q", tc.args, code, stderr)
		}
		last := f.requests[len(f.requests)-1]
		if last.Method != tc.method || last.URL.Path != tc.path {
			t.Errorf("%v: %s %s", tc.args, last.Method, last.URL.Path)
		}
		if !strings.Contains(out, tc.contains) {
			t.Errorf("%v: stdout %q lacks %q", tc.args, out, tc.contains)
		}
	}
}

// The view prints the head and then the Markdown as it is stored, so that the
// output can be piped straight back into `--body-file -`.
func TestPageViewPrintsTheStoredMarkdown(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, page }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, repository(t, "project = PLAN\n"), "page", "view", "architecture")

	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if !strings.HasPrefix(out, "PLAN/architecture  Architecture\n") {
		t.Fatalf("the head names the address and the title:\n%s", out)
	}
	if !strings.Contains(out, "updated: 2026-09-05T12:00:00Z by quiet-otter-42") {
		t.Fatalf("the head says when and by whom:\n%s", out)
	}
	if !strings.HasSuffix(out, "# The four layers\n\nDependencies point inward.\n") {
		t.Fatalf("the body is printed as it is stored:\n%q", out)
	}
}

func TestPageWritesSendWhatTheFlagsSay(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.Method == http.MethodPost:
			return 201, page
		case r.Method == http.MethodGet && r.URL.Path == "/projects/PLAN/pages":
			return 200, `[]`
		default:
			return 200, page
		}
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := repository(t, "project = PLAN\n")

	// The Markdown arrives over stdin, because an agent has it as Markdown already.
	code, _, stderr := run(t, server, dir, "page", "create", "architecture", "--title", "Architecture", "--body-file", "-")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	var created map[string]any
	_ = json.Unmarshal([]byte(f.bodies[0]), &created)
	if created["slug"] != "architecture" || created["title"] != "Architecture" || created["body"] != "" {
		t.Errorf("create body = %v", created)
	}
	// The guard is sent only when it is given, and quoted as the header wants it.
	code, _, stderr = run(t, server, dir, "page", "edit", "architecture", "--title", "New", "--if-match", "2026-09-05T12:00:00.000000Z")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if got := f.requests[len(f.requests)-1].Header.Get("If-Match"); got != `"2026-09-05T12:00:00.000000Z"` {
		t.Errorf("If-Match = %q", got)
	}

	// A rename sends the slug and nothing else: it is one act, not an edit.
	code, _, stderr = run(t, server, dir, "page", "rename", "architecture", "betriebshandbuch")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	var renamed map[string]any
	_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &renamed)
	if len(renamed) != 1 || renamed["slug"] != "betriebshandbuch" {
		t.Errorf("rename body = %v", renamed)
	}
	if f.requests[len(f.requests)-1].Header.Get("If-Match") != "" {
		t.Error("no --if-match, no header")
	}

	// `-q` is the full-text filter over title and body.
	if code, _, _ = run(t, server, dir, "page", "list", "-q", `"four layers"`); code != exit.OK {
		t.Fatalf("code %d", code)
	}
	query := f.requests[len(f.requests)-1].URL.Query()
	if got := query.Get("q"); got != `"four layers"` {
		t.Errorf("q = %q", got)
	}

	// Nothing typed, nothing sent: an empty filter is not a filter.
	if code, _, _ = run(t, server, dir, "page", "list"); code != exit.OK {
		t.Fatalf("code %d", code)
	}
	if f.requests[len(f.requests)-1].URL.Query().Has("q") {
		t.Error("an empty -q is not sent")
	}
}

func TestPageUsageMistakesAreExitTwo(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, page }}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := repository(t, "project = PLAN\n")

	for _, args := range [][]string{
		{"page", "create", "architecture"},
		{"page", "edit", "architecture"},
	} {
		if code, _, stderr := run(t, server, dir, args...); code != exit.Usage || stderr == "" {
			t.Errorf("%v: code %d, stderr %q", args, code, stderr)
		}
	}
}

// The stale refusal is exit 6, as docs/cli.md lays it down.
func TestPageEditIsExitSixWhenSomebodyCameBetween(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 412, `{"type":"/problems/stale","title":"stale","status":412,"detail":"PLAN/architecture changed."}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := run(t, server, repository(t, "project = PLAN\n"),
		"page", "edit", "architecture", "--title", "New", "--if-match", "2026-09-05T12:00:00.000000Z")

	if code != exit.Stale {
		t.Fatalf("code %d", code)
	}
	if !strings.Contains(stderr, "changed") {
		t.Errorf("stderr %q", stderr)
	}
}

// The instance serves the web application from the same port and falls back to
// `index.html` for every path no endpoint took, so an endpoint this build of ha
// knows and the instance does not answers 200 with a page of HTML. That is a
// success to every check there was, and every verb then dereferenced JSON the
// generated client had not filled in. Reported against `ha page list`; it was
// never about the list being empty.
func TestAnEndpointTheInstanceDoesNotHaveIsNotACrash(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, "<!doctype html><html><body>hostingaffe</body></html>"
	}, contentType: "text/html; charset=utf-8"}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, repository(t, "project = PLAN\n"), "page", "list")

	if code != exit.Unexpected {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if out != "" {
		t.Errorf("nothing goes to stdout: %q", out)
	}
	for _, want := range []string{"text/html", "/projects/PLAN/pages", "ha version"} {
		if !strings.Contains(stderr, want) {
			t.Errorf("stderr %q lacks %q", stderr, want)
		}
	}
}

// An empty list is an ordinary answer and stays one — this is what the report
// guessed the crash was, and it is worth holding still.
func TestAnEmptyWikiIsNotAnError(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, `[]` }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, repository(t, "project = PLAN\n"), "page", "list", "--project", "PLAN")

	if code != exit.OK || out != "" || stderr != "" {
		t.Fatalf("code %d, stdout %q, stderr %q", code, out, stderr)
	}
}

// A 204 carries no body and is not an unparsable answer.
func TestADeleteWithNoBodyIsStillASuccess(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 204, "" }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, repository(t, "project = PLAN\n"), "page", "delete", "architecture")

	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if !strings.Contains(out, "deleted") {
		t.Errorf("stdout %q", out)
	}
}
