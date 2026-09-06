package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const project = `{"key":"PLAN","name":"hostingaffe","triage_required":false,"review_required":true,"instructions_page":"agents","created_at":"2026-09-02T14:00:00.000000Z","updated_at":"2026-09-02T14:00:00.000000Z"}`
const label = `{"name":"bug","group":"kind","description":"Something that should work and does not."}`
const epic = `{"key":"PLAN-E2","project":"PLAN","title":"Backend","description":"The plan.","status":"open","author":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},"labels":[` + label + `],"progress":{"total":7,"closed":5,"done":4,"canceled":1},"created_at":"2026-09-02T14:00:00.000000Z","updated_at":"2026-09-02T14:00:00.000000Z","closed_at":null}`

func TestProjectLabelAndEpicVerbsHitTheirEndpoints(t *testing.T) {
	var f *fake
	f = &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.Method == http.MethodDelete:
			return 204, ""
		case r.Method == http.MethodPost && r.URL.Path == "/projects":
			return 201, project
		case r.Method == http.MethodPost && r.URL.Path == "/projects/PLAN/labels":
			return 201, label
		case r.Method == http.MethodPost && r.URL.Path == "/epics":
			return 201, epic
		case r.URL.Path == "/projects":
			return 200, `[` + project + `]`
		case r.URL.Path == "/projects/PLAN/labels":
			return 200, `[` + label + `,{"name":"cut-1","group":null,"description":null}]`
		case strings.HasPrefix(r.URL.Path, "/projects/PLAN/labels/"):
			return 200, label
		case strings.HasPrefix(r.URL.Path, "/projects/"):
			return 200, project
		case r.URL.Path == "/epics":
			return 200, `{"items":[{"key":"PLAN-E2","project":"PLAN","title":"Backend","status":"open","labels":["bug"],"progress":{"total":7,"closed":5,"done":4,"canceled":1},"created_at":"2026-09-02T14:00:00.000000Z","updated_at":"2026-09-02T14:00:00.000000Z","closed_at":null}],"total":1,"has_more":false,"next_cursor":null}`
		case r.URL.Path == "/issues":
			return 200, `{"items":[{"key":"PLAN-7","project":"PLAN","title":"Still open","status":"todo","ready":false,"priority":0,"labels":[],"epic":"PLAN-E2","assignee":null,"claim":null,"blocked_by":[],"open_questions":0,"open_blockers":0,"open_sub_issues":0,"created_at":"2026-09-02T14:00:00.000000Z","updated_at":"2026-09-02T14:00:00.000000Z","closed_at":null,"deleted_at":null,"deleted_by":null}],"total":1,"has_more":false,"next_cursor":null}`
		case strings.HasPrefix(r.URL.Path, "/issues/"):
			return 200, issue
		default:
			return 200, strings.Replace(epic, `"status":"open"`, `"status":"closed"`, 1)
		}
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := repository(t, "project = PLAN\n")

	cases := []struct {
		args   []string
		method string
		path   string
		body   map[string]any
		stdout string
	}{
		{[]string{"project", "create", "plan", "hostingaffe", "--review-required"}, "POST", "/projects", map[string]any{"key": "PLAN", "name": "hostingaffe", "review_required": true}, "PLAN  hostingaffe"},
		{[]string{"project", "list"}, "GET", "/projects", nil, "PLAN"},
		{[]string{"project", "view"}, "GET", "/projects/PLAN", nil, "review required: true"},
		{[]string{"project", "edit", "PLAN", "--triage-required", "true", "--name", "renamed"}, "PATCH", "/projects/PLAN", map[string]any{"triage_required": true, "name": "renamed"}, ""},
		{[]string{"project", "edit", "PLAN", "--instructions-page", "agents"}, "PATCH", "/projects/PLAN", map[string]any{"instructions_page": "agents"}, "instructions: agents"},
		{[]string{"project", "edit", "PLAN", "--instructions-page", "none"}, "PATCH", "/projects/PLAN", map[string]any{"instructions_page": nil}, ""},
		{[]string{"project", "delete", "PLAN", "--confirm", "plan"}, "DELETE", "/projects/PLAN", nil, "PLAN deleted"},
		{[]string{"project", "restore", "PLAN"}, "POST", "/projects/PLAN/restore", nil, "PLAN"},
		{[]string{"label", "list"}, "GET", "/projects/PLAN/labels", nil, "Something that should work"},
		{[]string{"label", "create", "area:infra", "--group", "area", "--description", "Compose, CI."}, "POST", "/projects/PLAN/labels", map[string]any{"name": "area:infra", "group": "area", "description": "Compose, CI."}, ""},
		{[]string{"label", "edit", "bug", "--group", "none", "--description", "x"}, "PATCH", "/projects/PLAN/labels/bug", map[string]any{"group": nil, "description": "x"}, ""},
		{[]string{"label", "delete", "bug"}, "DELETE", "/projects/PLAN/labels/bug", nil, "bug deleted"},
		{[]string{"label", "restore", "bug"}, "POST", "/projects/PLAN/labels/bug/restore", nil, "bug"},
		{[]string{"epic", "create", "Backend", "--label", "bug"}, "POST", "/epics", map[string]any{"project": "PLAN", "title": "Backend"}, "PLAN-E2  Backend"},
		{[]string{"epic", "list", "--status", "all"}, "GET", "/epics", nil, "5 of 7 closed · 4 done · 1 canceled"},
		{[]string{"epic", "view", "PLAN-E2"}, "GET", "/epics/PLAN-E2", nil, "The plan."},
		{[]string{"epic", "edit", "PLAN-E2", "--title", "Backend and data model", "--if-match", "2026-09-02T14:00:00.000000Z"}, "PATCH", "/epics/PLAN-E2", map[string]any{"title": "Backend and data model"}, ""},
		{[]string{"epic", "reopen", "PLAN-E2"}, "POST", "/epics/PLAN-E2/reopen", nil, ""},
		{[]string{"epic", "delete", "PLAN-E2"}, "DELETE", "/epics/PLAN-E2", nil, "PLAN-E2 deleted"},
		{[]string{"epic", "restore", "PLAN-E2"}, "POST", "/epics/PLAN-E2/restore", nil, ""},
	}

	for _, c := range cases {
		code, out, errOut := run(t, server, dir, c.args...)
		if code != exit.OK {
			t.Fatalf("%v: code %d, stderr %s", c.args, code, errOut)
		}
		last := f.requests[len(f.requests)-1]
		if last.Method != c.method || last.URL.Path != c.path {
			t.Errorf("%v: %s %s, want %s %s", c.args, last.Method, last.URL.Path, c.method, c.path)
		}
		if c.body != nil {
			var body map[string]any
			_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body)
			for k, want := range c.body {
				if got, present := body[k]; !present || got != want {
					t.Errorf("%v: body[%s] = %v (present %v), want %v", c.args, k, got, present, want)
				}
			}
		}
		if c.stdout != "" && !strings.Contains(out, c.stdout) {
			t.Errorf("%v: stdout %q lacks %q", c.args, out, c.stdout)
		}
	}

	// The guard on the living document (`docs/api.md`, Concurrency on text
	// fields): the header is sent quoted, and a `stale` refusal is exit 6, so
	// that a script can branch on it rather than parse a sentence.
	var guarded *http.Request
	for _, r := range f.requests {
		if r.Method == http.MethodPatch && r.URL.Path == "/epics/PLAN-E2" {
			guarded = r
		}
	}
	if guarded == nil || guarded.Header.Get("If-Match") != `"2026-09-02T14:00:00.000000Z"` {
		t.Errorf("epic edit --if-match sends no quoted header: %v", guarded)
	}

	stale := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method == http.MethodPatch {
			return 412, `{"type":"/problems/stale","title":"stale","status":412,"detail":"PLAN-E2 changed.","current":` + epic + `}`
		}
		return 200, epic
	}}
	staleServer := httptest.NewServer(stale.handler())
	defer staleServer.Close()
	if code, _, errOut := run(t, staleServer, dir, "epic", "edit", "PLAN-E2", "--title", "x", "--if-match", "2026-09-02T14:00:00.000000Z"); code != exit.Stale {
		t.Errorf("stale epic edit: code %d, want %d; stderr %q", code, exit.Stale, errOut)
	}

	// Deleting a project without the key typed again is a usage mistake, before any request.
	before := len(f.requests)
	code, _, errOut := run(t, server, dir, "project", "delete", "PLAN")
	if code != exit.Usage || !strings.Contains(errOut, "--confirm PLAN") || len(f.requests) != before {
		t.Errorf("delete without confirm: code %d, stderr %q, requests %d", code, errOut, len(f.requests)-before)
	}

	// Closing an epic lists what is open on stderr and, on a flag, parks it in the same command.
	code, out, errOut := run(t, server, dir, "epic", "close", "PLAN-E2")
	if code != exit.OK || !strings.Contains(errOut, "1 issue(s) still open") || !strings.Contains(errOut, "PLAN-7") || !strings.Contains(out, "PLAN-E2") {
		t.Errorf("close: code %d, stdout %q, stderr %q", code, out, errOut)
	}
	before = len(f.requests)
	code, _, errOut = run(t, server, dir, "epic", "close", "PLAN-E2", "--park-open")
	if code != exit.OK || errOut != "" {
		t.Errorf("close --park-open: code %d, stderr %q", code, errOut)
	}
	var parked bool
	keys := map[string]bool{}
	for _, r := range f.requests[before:] {
		if r.Method == http.MethodPatch && r.URL.Path == "/issues/PLAN-7" {
			parked = true
		}
		if k := r.Header.Get("Idempotency-Key"); k != "" {
			if keys[k] {
				t.Errorf("two writes of one command share the key %s", k)
			}
			keys[k] = true
		}
	}
	if !parked {
		t.Error("--park-open parks the open issue in the same command")
	}
	before = len(f.requests)
	_, _, _ = run(t, server, dir, "epic", "close", "PLAN-E2", "--cancel-open")
	var canceled bool
	for i, r := range f.requests[before:] {
		if r.Method == http.MethodPost && r.URL.Path == "/issues/PLAN-7/close" && strings.Contains(f.bodies[before+i], "canceled") {
			canceled = true
		}
	}
	if !canceled {
		t.Error("--cancel-open cancels the open issue in the same command")
	}
}
