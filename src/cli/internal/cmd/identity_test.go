package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

func TestIdentityVerbsPrintSecretsOnceAndHitTheirEndpoints(t *testing.T) {
	const me = `{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer","administrator":true,"owner":null,"token":{"prefix":"ha_a1b2c","created_at":"2026-09-02T14:00:00.000000Z"}}`
	const agentMe = `{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42","administrator":false,"owner":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},"token":{"prefix":"ha_secre","created_at":"2026-09-02T14:00:00.000000Z"},"metadata":{"kind":"codex","harness":null,"environment":"container","version":"1.2.3"},"metadata_reported_at":"2026-09-04T07:30:00Z"}`
	const issued = `{"id":"0198e0c0-0000-7000-8000-00000000000b","prefix":"ha_secre","secret":"ha_secret-shown-once-and-nowhere-else-at-all-43chars","created_at":"2026-09-02T14:00:00.000000Z"}`
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.Method == http.MethodPatch && r.URL.Path == "/api/me/metadata":
			return 200, agentMe
		case r.URL.Path == "/api/me":
			return 200, me
		case r.URL.Path == "/api/version":
			return 200, `{"version":"0.0.0-dev"}`
		case r.Method == http.MethodPost && r.URL.Path == "/api/users":
			return 201, `{"id":"0198e0c0-0000-7000-8000-000000000003","kind":"user","name":"other","email":"other@example.test","state":"invited","administrator":false,"created_at":"2026-09-02T14:00:00.000000Z"}`
		case r.Method == http.MethodPost && strings.HasPrefix(r.URL.Path, "/api/users/"):
			return 200, `{"id":"0198e0c0-0000-7000-8000-000000000003","kind":"user","name":"other","email":"other@example.test","state":"deactivated","administrator":false,"created_at":"2026-09-02T14:00:00.000000Z"}`
		case r.URL.Path == "/api/users":
			return 200, `[{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer","administrator":true,"created_at":"2026-09-02T14:00:00.000000Z"}]`
		case r.Method == http.MethodPost && r.URL.Path == "/api/agents":
			return 201, `{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42","owner":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},"created_at":"2026-09-02T14:00:00.000000Z","token":` + issued + `}`
		case r.URL.Path == "/api/agents":
			return 200, `[{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42","owner":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},"created_at":"2026-09-02T14:00:00.000000Z","token":{"id":"0198e0c0-0000-7000-8000-00000000000b","prefix":"ha_secre","created_at":"2026-09-02T14:00:00.000000Z","revoked_at":null},"metadata":{"kind":"codex","harness":"cli","environment":"container","version":"1.2.3"},"metadata_reported_at":"2026-09-04T07:30:00Z"}]`
		case r.Method == http.MethodPatch:
			return 200, `{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"brisk-heron-7","owner":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},"created_at":"2026-09-02T14:00:00.000000Z","token":{"id":"0198e0c0-0000-7000-8000-00000000000b","prefix":"ha_secre","created_at":"2026-09-02T14:00:00.000000Z","revoked_at":null}}`
		case r.Method == http.MethodDelete:
			return 204, ""
		case r.Method == http.MethodPost && r.URL.Path == "/api/tokens":
			return 201, issued
		case r.URL.Path == "/api/tokens":
			return 200, `[{"id":"0198e0c0-0000-7000-8000-00000000000b","prefix":"ha_secre","created_at":"2026-09-02T14:00:00.000000Z","revoked_at":null}]`
		}
		return 404, ""
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	cases := []struct {
		args   []string
		method string
		path   string
		body   map[string]any
		stdout string
		stderr string
	}{
		{[]string{"me"}, "GET", "/api/me", nil, "maintainer (user)  administrator", ""},
		{[]string{"me", "set", "--kind", "codex", "--harness", "none", "--environment", "container", "--version", "1.2.3"}, "PATCH", "/api/me/metadata", map[string]any{"kind": "codex", "harness": nil, "environment": "container", "version": "1.2.3"}, "metadata:", ""},
		{[]string{"version"}, "GET", "/api/version", nil, "ha 0.0.0-dev\nhostingaffe 0.0.0-dev", ""},
		{[]string{"user", "create", "other", "--email", "other@example.test"}, "POST", "/api/users", map[string]any{"name": "other", "email": "other@example.test"}, "other invited", ""},
		{[]string{"user", "list"}, "GET", "/api/users", nil, "maintainer", ""},
		{[]string{"agent", "create", "--name", "quiet-otter-42"}, "POST", "/api/agents", map[string]any{"name": "quiet-otter-42"}, "token: ha_secret-shown-once", "shown once"},
		{[]string{"agent", "list"}, "GET", "/api/agents", nil, "quiet-otter-42", ""},
		{[]string{"agent", "view", "0198e0c0-0000-7000-8000-000000000001"}, "GET", "/api/agents", nil, "environment: container", ""},
		{[]string{"agent", "rename", "0198e0c0-0000-7000-8000-000000000001", "--name", "brisk-heron-7"}, "PATCH", "/api/agents/0198e0c0-0000-7000-8000-000000000001", map[string]any{"name": "brisk-heron-7"}, "renamed to brisk-heron-7", ""},
		{[]string{"agent", "revoke", "0198e0c0-0000-7000-8000-000000000001"}, "DELETE", "/api/agents/0198e0c0-0000-7000-8000-000000000001", nil, "revoked", ""},
		// The same three by name. A name is unique across users and agents and
		// never shaped like a UUID, so it goes into the path as it stands; the
		// view is the one that resolves in the client, because the list is all
		// there is to read.
		{[]string{"agent", "view", "quiet-otter-42"}, "GET", "/api/agents", nil, "environment: container", ""},
		{[]string{"agent", "view", "QUIET-OTTER-42"}, "GET", "/api/agents", nil, "environment: container", ""},
		{[]string{"agent", "rename", "quiet-otter-42", "--name", "brisk-heron-7"}, "PATCH", "/api/agents/quiet-otter-42", map[string]any{"name": "brisk-heron-7"}, "renamed to brisk-heron-7", ""},
		{[]string{"agent", "revoke", "quiet-otter-42"}, "DELETE", "/api/agents/quiet-otter-42", nil, "revoked", ""},
		{[]string{"user", "deactivate", "other"}, "POST", "/api/users/other/deactivate", nil, "other deactivated", ""},
		{[]string{"token", "create"}, "POST", "/api/tokens", nil, "token: ha_secret-shown-once", "shown once"},
		{[]string{"token", "list"}, "GET", "/api/tokens", nil, "ha_secre…", ""},
		{[]string{"token", "revoke", "0198e0c0-0000-7000-8000-00000000000b"}, "DELETE", "/api/tokens/0198e0c0-0000-7000-8000-00000000000b", nil, "revoked", ""},
	}
	for _, c := range cases {
		code, out, errOut := run(t, server, c.args...)
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
				if body[k] != want {
					t.Errorf("%v: body[%s] = %v, want %v", c.args, k, body[k], want)
				}
			}
		}
		if !strings.Contains(out, c.stdout) {
			t.Errorf("%v: stdout %q lacks %q", c.args, out, c.stdout)
		}
		if c.stderr == "" && errOut != "" || c.stderr != "" && !strings.Contains(errOut, c.stderr) {
			t.Errorf("%v: stderr %q, want %q", c.args, errOut, c.stderr)
		}
		if strings.Contains(errOut, "ha_secret") {
			t.Errorf("%v: the secret is on stdout and nowhere else", c.args)
		}
	}

	// A token is the one thing here without a name, so it is the one place
	// where an argument that is not a UUID is still a usage error.
	code, _, errOut := run(t, server, "token", "revoke", "not-an-id")
	if code != exit.Usage || !strings.Contains(errOut, "not a token id") {
		t.Errorf("bad token id: code %d, stderr %q", code, errOut)
	}
}

func TestVersionPrintsBothSidesAndSaysWhenTheyDoNotFit(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, `{"version":"0.0.0-dev"}` }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, _ := run(t, server, "version", "--json")
	if code != exit.OK || !strings.Contains(out, `"ha": "0.0.0-dev"`) || !strings.Contains(out, `"hostingaffe": "0.0.0-dev"`) {
		t.Fatalf("code %d, out %s", code, out)
	}
	// The skew itself cannot be provoked from a development build, whose version
	// is never checked; the rule is tested in the version package, and the
	// message route through `ha version` is what this test would need a
	// released build for.
}
