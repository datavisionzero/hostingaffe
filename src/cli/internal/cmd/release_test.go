package cmd

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const release = `{"name":"v1.0.0","status":"published","description":"First release.","published_at":"2026-09-04T12:00:00Z","published_by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},"issues":[{"key":"PLAN-1","project":"PLAN","title":"Parent","status":"done","ready":true,"priority":2,"labels":[],"epic":null,"parent":null,"release":"v1.0.0","assignee":null,"claim":null,"blocked_by":[],"open_questions":0,"open_blockers":0,"open_sub_issues":0,"created_at":"2026-09-04T10:00:00Z","updated_at":"2026-09-04T11:00:00Z","closed_at":"2026-09-04T11:00:00Z","deleted_at":null,"deleted_by":null},{"key":"PLAN-2","project":"PLAN","title":"Child","status":"done","ready":true,"priority":2,"labels":[],"epic":null,"parent":"PLAN-1","release":"v1.0.0","assignee":null,"claim":null,"blocked_by":[],"open_questions":0,"open_blockers":0,"open_sub_issues":0,"created_at":"2026-09-04T10:00:00Z","updated_at":"2026-09-04T11:00:00Z","closed_at":"2026-09-04T11:00:00Z","deleted_at":null,"deleted_by":null}]}`

func TestReleaseVerbsAndMarkdownNotes(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.Method == http.MethodGet && r.URL.Path == "/projects/PLAN/releases":
			return 200, `[{"name":"unreleased","status":"open","description":"","published_at":null,"published_by":null,"issues":0}]`
		case r.Method == http.MethodPost && r.URL.Path == "/projects/PLAN/releases/publish":
			return 201, release
		default:
			return 200, release
		}
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := repository(t, "project = PLAN\n")

	for _, tc := range []struct {
		args                   []string
		method, path, contains string
	}{
		{[]string{"release", "list"}, "GET", "/projects/PLAN/releases", "unreleased"},
		{[]string{"release", "view", "v1.0.0"}, "GET", "/projects/PLAN/releases/v1.0.0", "First release."},
		{[]string{"release", "publish", "v1.0.0"}, "POST", "/projects/PLAN/releases/publish", "v1.0.0"},
		{[]string{"release", "notes", "v1.0.0"}, "GET", "/projects/PLAN/releases/v1.0.0", "  - PLAN-2 Child"},
		// VISION 7: a ticket that has not shipped yet simply does not belong,
		// and the newest publication may be corrected.
		{[]string{"release", "add", "PLAN-1"}, "PUT", "/projects/PLAN/releases/unreleased/issues/PLAN-1", "v1.0.0"},
		{[]string{"release", "remove", "PLAN-1"}, "DELETE", "/projects/PLAN/releases/unreleased/issues/PLAN-1", "v1.0.0"},
		{[]string{"release", "rename", "v1.0.O", "v1.0.0"}, "PATCH", "/projects/PLAN/releases/v1.0.O", "v1.0.0"},
		{[]string{"release", "retract", "v1.0.0"}, "POST", "/projects/PLAN/releases/v1.0.0/retract", "v1.0.0"},
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
