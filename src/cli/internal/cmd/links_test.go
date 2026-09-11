package cmd

import (
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

func TestReferencesInABodyAreTheRecordsOwnSchemes(t *testing.T) {
	body := strings.Join([]string{
		"Read [the runbook](page:backup-restore) on [ex44](machine:ex44).",
		"[caddy](software:caddy) runs as [app-1](installation:app-1#ports).",
		"Not this: [a](https://example.org), [b](../decisions/x.md), [c](#heading), [d](page:Not-A-Slug).",
		"",
		"```sh",
		"echo '[e](page:in-a-fence)'",
		"```",
		"",
		"[f]: page:by-reference",
	}, "\n")

	found := referencesIn("setup", body)

	want := []reference{
		{Page: "setup", Text: "the runbook", Target: "page:backup-restore", kind: "page", address: "backup-restore"},
		{Page: "setup", Text: "ex44", Target: "machine:ex44", kind: "machine", address: "ex44"},
		{Page: "setup", Text: "caddy", Target: "software:caddy", kind: "software", address: "caddy"},
		{Page: "setup", Text: "app-1", Target: "installation:app-1#ports", kind: "installation", address: "app-1"},
		{Page: "setup", Text: "f", Target: "page:by-reference", kind: "page", address: "by-reference"},
	}

	if len(found) != len(want) {
		t.Fatalf("found %d references: %+v", len(found), found)
	}
	for i, one := range found {
		if one != want[i] {
			t.Errorf("reference %d is %+v, not %+v", i, one, want[i])
		}
	}
}

// A page nobody wrote, a machine nobody has: the check is what says so, because
// nothing validates a body (ADR 0007).
func TestPageCheckNamesTheReferencesThatPointAtNothing(t *testing.T) {
	setup := fmt.Sprintf(`{"slug":"setup","title":"Setup","body":%s,"kind":"note","attached_to":null,
	"author":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
	"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
	"created_at":"2026-09-05T10:00:00.000000Z","updated_at":"2026-09-05T12:00:00.000000Z"}`,
		strconv.Quote("It runs on [ex44](machine:ex44); see [the runbook](page:nowhere)."))

	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch r.URL.Path {
		case "/api/pages":
			return 200, `[{"slug":"setup","title":"Setup","kind":"note","attached_to":null,
			"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
			"created_at":"2026-09-05T10:00:00Z","updated_at":"2026-09-05T12:00:00Z"}]`
		case "/api/pages/setup":
			return 200, setup
		case "/api/machines":
			return 200, `[{"key":"ex44","kind":"dedicated","status":"active","name":"The big one",
			"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
			"created_at":"2026-09-05T10:00:00Z","updated_at":"2026-09-05T12:00:00Z"}]`
		}
		return 404, `{"type":"about:blank","title":"not found","status":404}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "page", "check", "--json")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	var dead []map[string]string
	if err := json.Unmarshal([]byte(out), &dead); err != nil {
		t.Fatalf("not JSON: %v (%s)", err, out)
	}
	if len(dead) != 1 {
		t.Fatalf("%d dead references: %s", len(dead), out)
	}
	if dead[0]["target"] != "page:nowhere" || dead[0]["page"] != "setup" || dead[0]["text"] != "the runbook" {
		t.Errorf("the dead reference is %v", dead[0])
	}

	// The machine was asked for once and the software never: a wiki that links
	// no software costs no call about software.
	asked := map[string]int{}
	for _, r := range f.requests {
		asked[r.URL.Path]++
	}
	if asked["/api/machines"] != 1 || asked["/api/software"] != 0 {
		t.Errorf("asked %v", asked)
	}
}

// The report is what a person reads: the page it stands in, what it points at,
// and the words it was written as.
func TestPageCheckPrintsOneLinePerDeadReference(t *testing.T) {
	setup := fmt.Sprintf(`{"slug":"setup","title":"Setup","body":%s,"kind":"note","attached_to":null,
	"author":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
	"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
	"created_at":"2026-09-05T10:00:00.000000Z","updated_at":"2026-09-05T12:00:00.000000Z"}`,
		strconv.Quote("See [the runbook](page:nowhere)."))

	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Path == "/api/pages" {
			return 200, `[]`
		}
		return 200, setup
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "page", "check", "setup")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	for _, want := range []string{"setup", "page:nowhere", "the runbook"} {
		if !strings.Contains(out, want) {
			t.Errorf("the report does not say %q: %q", want, out)
		}
	}

	// Naming the page is one read and no listing of the wiki.
	if f.requests[0].URL.Path != "/api/pages/setup" {
		t.Errorf("first asked %s", f.requests[0].URL.Path)
	}
}
