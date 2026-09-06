package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const page = `{"slug":"architecture","title":"Architecture","body":"# The four layers\n\nDependencies point inward.",
"kind":"decision","attached_to":{"kind":"machine","key":"caddy"},
"author":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
"created_at":"2026-09-05T10:00:00.000000Z","updated_at":"2026-09-05T12:00:00.000000Z"}`

func TestPageVerbsReachTheRightAddresses(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method == http.MethodGet && r.URL.Path == "/api/pages" {
			return 200, `[{"slug":"architecture","title":"Architecture","kind":"note","attached_to":null,
			"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
			"created_at":"2026-09-05T10:00:00Z","updated_at":"2026-09-05T12:00:00Z"}]`
		}
		if r.Method == http.MethodPost && r.URL.Path == "/api/pages" {
			return 201, page
		}
		if r.Method == http.MethodDelete {
			return 204, ""
		}
		return 200, page
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, tc := range []struct {
		args                   []string
		method, path, contains string
	}{
		{[]string{"page", "list"}, "GET", "/api/pages", "architecture"},
		{[]string{"page", "view", "architecture"}, "GET", "/api/pages/architecture", "# The four layers"},
		{[]string{"page", "create", "architecture", "--title", "Architecture"}, "POST", "/api/pages", "architecture"},
		{[]string{"page", "edit", "architecture", "--title", "The four layers"}, "PATCH", "/api/pages/architecture", "architecture"},
		{[]string{"page", "rename", "architecture", "betriebshandbuch"}, "PATCH", "/api/pages/architecture", "architecture"},
		{[]string{"page", "delete", "architecture"}, "DELETE", "/api/pages/architecture", "ha page restore architecture"},
		{[]string{"page", "restore", "architecture"}, "POST", "/api/pages/architecture/restore", "architecture"},
	} {
		code, out, stderr := run(t, server, tc.args...)
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

	code, out, stderr := run(t, server, "page", "view", "architecture")

	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if !strings.HasPrefix(out, "architecture  Architecture\n") {
		t.Fatalf("the head names the address and the title:\n%s", out)
	}
	if !strings.Contains(out, "kind: decision  hangs on: machine caddy") {
		t.Fatalf("the head says the kind and what it hangs on:\n%s", out)
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
		case r.Method == http.MethodGet && r.URL.Path == "/api/pages":
			return 200, `[]`
		default:
			return 200, page
		}
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	// The Markdown arrives over stdin, because an agent has it as Markdown already.
	code, _, stderr := run(t, server, "page", "create", "architecture", "--title", "Architecture", "--body-file", "-")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	var created map[string]any
	_ = json.Unmarshal([]byte(f.bodies[0]), &created)
	if created["slug"] != "architecture" || created["title"] != "Architecture" || created["body"] != "" {
		t.Errorf("create body = %v", created)
	}
	// The guard is sent only when it is given, and quoted as the header wants it.
	code, _, stderr = run(t, server, "page", "edit", "architecture", "--title", "New", "--if-match", "2026-09-05T12:00:00.000000Z")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if got := f.requests[len(f.requests)-1].Header.Get("If-Match"); got != `"2026-09-05T12:00:00.000000Z"` {
		t.Errorf("If-Match = %q", got)
	}

	// A rename sends the slug and nothing else: it is one act, not an edit.
	code, _, stderr = run(t, server, "page", "rename", "architecture", "betriebshandbuch")
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
	if code, _, _ = run(t, server, "page", "list", "-q", `"four layers"`); code != exit.OK {
		t.Fatalf("code %d", code)
	}
	query := f.requests[len(f.requests)-1].URL.Query()
	if got := query.Get("q"); got != `"four layers"` {
		t.Errorf("q = %q", got)
	}

	// Nothing typed, nothing sent: an empty filter is not a filter.
	if code, _, _ = run(t, server, "page", "list"); code != exit.OK {
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

	for _, args := range [][]string{
		{"page", "create", "architecture"},
		{"page", "edit", "architecture"},
		{"page", "create", "architecture", "--title", "T", "--machine", "caddy", "--installation", "logaffe-prod"},
		{"page", "edit", "architecture", "--machine", "caddy", "--installation", "logaffe-prod"},
		{"page", "edit", "architecture", "--machine", "caddy", "--detach"},
		{"page", "list", "--machine", "caddy", "--installation", "logaffe-prod"},
	} {
		if code, _, stderr := run(t, server, args...); code != exit.Usage || stderr == "" {
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

	code, _, stderr := run(t, server,
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

	code, out, stderr := run(t, server, "page", "list")

	if code != exit.Unexpected {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if out != "" {
		t.Errorf("nothing goes to stdout: %q", out)
	}
	for _, want := range []string{"text/html", "/api/pages", "ha version"} {
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

	code, out, stderr := run(t, server, "page", "list")

	if code != exit.OK || out != "" || stderr != "" {
		t.Fatalf("code %d, stdout %q, stderr %q", code, out, stderr)
	}
}

// A 204 carries no body and is not an unparsable answer.
func TestADeleteWithNoBodyIsStillASuccess(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 204, "" }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "page", "delete", "architecture")

	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if !strings.Contains(out, "deleted") {
		t.Errorf("stdout %q", out)
	}
}

// The two fields HOST-17 gave the record reach the wire, and the anchor goes as
// the kind and the key together — "machine caddy" and "software caddy" are
// different things, and a key alone would not say which.
func TestPageWritesCarryTheKindAndTheAnchor(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method == http.MethodPost {
			return 201, page
		}
		return 200, page
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	sent := func(t *testing.T, args ...string) map[string]any {
		t.Helper()
		code, _, stderr := run(t, server, args...)
		if code != exit.OK || stderr != "" {
			t.Fatalf("%v: code %d, stderr %q", args, code, stderr)
		}
		var body map[string]any
		if err := json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body); err != nil {
			t.Fatalf("%v: %v", args, err)
		}
		return body
	}

	created := sent(t, "page", "create", "backup-restore", "--title", "Backup and restore",
		"--kind", "runbook", "--machine", "caddy")
	if created["kind"] != "runbook" {
		t.Errorf("kind = %v", created["kind"])
	}
	if anchor, _ := created["attached_to"].(map[string]any); anchor["kind"] != "machine" || anchor["key"] != "caddy" {
		t.Errorf("attached_to = %v", created["attached_to"])
	}

	// No --kind is no `kind`: the default is the instance's to apply, not ha's
	// to guess, and a page created by an older ha still lands as `note`.
	if plain := sent(t, "page", "create", "notes", "--title", "Notes"); plain["kind"] != nil {
		t.Errorf("kind = %v, want absent", plain["kind"])
	}

	changed := sent(t, "page", "edit", "backup-restore", "--kind", "decision", "--installation", "logaffe-prod")
	if changed["kind"] != "decision" {
		t.Errorf("kind = %v", changed["kind"])
	}
	if anchor, _ := changed["attached_to"].(map[string]any); anchor["kind"] != "installation" || anchor["key"] != "logaffe-prod" {
		t.Errorf("attached_to = %v", changed["attached_to"])
	}

	// Leaving the flags off lets the page hang where it hangs; only --detach
	// says out loud that it should hang on nothing, and that is the null.
	title := sent(t, "page", "edit", "backup-restore", "--title", "Restore")
	if _, said := title["attached_to"]; said {
		t.Errorf("an untouched anchor is not sent: %v", title)
	}
	detached := sent(t, "page", "edit", "backup-restore", "--detach")
	value, said := detached["attached_to"]
	if !said || value != nil {
		t.Errorf("--detach sends null: %v", detached)
	}
}

// Filtering by both fields, because otherwise they carry nothing at the console.
func TestPageListFiltersByKindAndAnchor(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, `[]` }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, tc := range []struct {
		args  []string
		param string
		want  string
	}{
		{[]string{"page", "list", "--kind", "decision"}, "kind", "decision"},
		{[]string{"page", "list", "--machine", "caddy"}, "machine", "caddy"},
		{[]string{"page", "list", "--installation", "logaffe-prod"}, "installation", "logaffe-prod"},
	} {
		if code, _, stderr := run(t, server, tc.args...); code != exit.OK || stderr != "" {
			t.Fatalf("%v: code %d, stderr %q", tc.args, code, stderr)
		}
		query := f.requests[len(f.requests)-1].URL.Query()
		if got := query.Get(tc.param); got != tc.want {
			t.Errorf("%v: %s = %q", tc.args, tc.param, got)
		}
	}

	// An empty filter is not a filter, as it already was for -q.
	if code, _, _ := run(t, server, "page", "list"); code != exit.OK {
		t.Fatal("code")
	}
	query := f.requests[len(f.requests)-1].URL.Query()
	for _, name := range []string{"kind", "machine", "installation"} {
		if query.Has(name) {
			t.Errorf("an empty --%s is not sent", name)
		}
	}
}

// The list shows what the fields say, so that what was set is visible without
// viewing every page one at a time. A page of the instance hangs on a dash.
func TestPageListShowsTheKindAndTheAnchor(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `[{"slug":"backup-restore","title":"Backup and restore","kind":"runbook",
		"attached_to":{"kind":"machine","key":"caddy"},
		"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
		"created_at":"2026-09-05T10:00:00Z","updated_at":"2026-09-05T12:00:00Z"},
		{"slug":"tailscale-for-management","title":"Tailscale","kind":"decision","attached_to":null,
		"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
		"created_at":"2026-09-05T10:00:00Z","updated_at":"2026-09-05T12:00:00Z"}]`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "page", "list")

	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	lines := strings.Split(strings.TrimSuffix(out, "\n"), "\n")
	if len(lines) != 2 {
		t.Fatalf("two pages, %d lines:\n%s", len(lines), out)
	}
	for _, want := range []string{"runbook", "machine caddy", "Backup and restore"} {
		if !strings.Contains(lines[0], want) {
			t.Errorf("%q lacks %q", lines[0], want)
		}
	}
	for _, want := range []string{"decision", "-", "Tailscale"} {
		if !strings.Contains(lines[1], want) {
			t.Errorf("%q lacks %q", lines[1], want)
		}
	}
}
