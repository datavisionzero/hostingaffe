package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const project = `{"key":"PLAN","name":"hostingaffe","instructions_page":"agents","created_at":"2026-09-02T14:00:00.000000Z","updated_at":"2026-09-02T14:00:00.000000Z"}`

func TestProjectVerbsHitTheirEndpoints(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.Method == http.MethodDelete:
			return 204, ""
		case r.URL.Path == "/projects" && r.Method == http.MethodPost:
			return 201, project
		case r.URL.Path == "/projects":
			return 200, `[` + project + `]`
		default:
			return 200, project
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
		{[]string{"project", "create", "plan", "hostingaffe"}, "POST", "/projects", map[string]any{"key": "PLAN", "name": "hostingaffe"}, "PLAN  hostingaffe"},
		{[]string{"project", "list"}, "GET", "/projects", nil, "PLAN"},
		{[]string{"project", "view"}, "GET", "/projects/PLAN", nil, "PLAN  hostingaffe"},
		{[]string{"project", "edit", "PLAN", "--name", "renamed"}, "PATCH", "/projects/PLAN", map[string]any{"name": "renamed"}, ""},
		{[]string{"project", "edit", "PLAN", "--instructions-page", "agents"}, "PATCH", "/projects/PLAN", map[string]any{"instructions_page": "agents"}, "instructions: agents"},
		{[]string{"project", "edit", "PLAN", "--instructions-page", "none"}, "PATCH", "/projects/PLAN", map[string]any{"instructions_page": nil}, ""},
		{[]string{"project", "delete", "PLAN", "--confirm", "plan"}, "DELETE", "/projects/PLAN", nil, "PLAN deleted"},
		{[]string{"project", "restore", "PLAN"}, "POST", "/projects/PLAN/restore", nil, "PLAN"},
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

	// Deleting a project without the key typed again is a usage mistake, before any request.
	before := len(f.requests)
	code, _, errOut := run(t, server, dir, "project", "delete", "PLAN")
	if code != exit.Usage || !strings.Contains(errOut, "--confirm PLAN") || len(f.requests) != before {
		t.Errorf("delete without confirm: code %d, stderr %q, requests %d", code, errOut, len(f.requests)-before)
	}
}
