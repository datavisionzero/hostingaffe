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
	deploymentJSON = `{"installation":"logaffe-prod","number":3,"version":"1.4.0","previous":"1.3.2",
"ref":"ghcr.io/datavisionzero/logaffe@sha256:abc","files":[{"path":"compose.yml","revision":4},
{"path":"sites/logaffe.caddy","revision":2}],"at":"2026-09-05T12:00:00.000000Z",
"by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"ticket":"LOG-42","note":"Rolled forward after the schema migration.",
"created_at":"2026-09-05T12:00:01.000000Z",
"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
"updated_at":"2026-09-06T08:00:00.000000Z"}`

	deploymentsJSON = `[{"number":3,"version":"1.4.0","previous":"1.3.2","ref":"ghcr.io/datavisionzero/logaffe@sha256:abc",
"at":"2026-09-05T12:00:00Z","by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"ticket":"LOG-42","updated_at":"2026-09-05T12:00:01Z"}]`
)

func deployments() *fake {
	return &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.Method == http.MethodDelete:
			return 204, ""
		case strings.HasSuffix(r.URL.Path, "/history"):
			return 200, historyJSON
		case strings.HasSuffix(r.URL.Path, "/deployments"):
			if r.Method == http.MethodPost {
				return 201, deploymentJSON
			}
			return 200, deploymentsJSON
		default:
			return 200, deploymentJSON
		}
	}}
}

// Recording is the bare verb, because that is what an agent types after the
// work; everything else is a subcommand under it.
func TestDeploymentVerbsReachTheRightAddresses(t *testing.T) {
	f := deployments()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	const under = "/api/installations/logaffe-prod/deployments"

	for _, tc := range []struct {
		args                   []string
		method, path, contains string
	}{
		{[]string{"deploy", "logaffe-prod", "--version", "1.4.0"}, "POST", under, "logaffe-prod #3  1.4.0"},
		{[]string{"deploy", "list", "--inst", "logaffe-prod"}, "GET", under, "#3"},
		{[]string{"deploy", "view", "logaffe-prod", "3"}, "GET", under + "/3", "1.4.0"},
		{[]string{"deploy", "set", "logaffe-prod", "3", "--ticket", "LOG-43"}, "PATCH", under + "/3", "1.4.0"},
		{[]string{"deploy", "delete", "logaffe-prod", "3"}, "DELETE", under + "/3", "ha deploy restore logaffe-prod 3"},
		{[]string{"deploy", "restore", "logaffe-prod", "3"}, "POST", under + "/3/restore", "1.4.0"},
		{[]string{"deploy", "history", "logaffe-prod", "3"}, "GET", under + "/3/history", "dist-upgrade"},
		// The object keeps the glossary's word; `deploy` is only the short form.
		{[]string{"deployment", "view", "logaffe-prod", "3"}, "GET", under + "/3", "1.4.0"},
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

// The note is the deployment's own field — why, what was checked, what went
// wrong — and an agent has it as Markdown already.
func TestRecordingSendsWhatItWasGiven(t *testing.T) {
	f := deployments()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := run(t, server, "deploy", "logaffe-prod",
		"--version", "1.4.0", "--ref", "ghcr.io/datavisionzero/logaffe@sha256:abc",
		"--ticket", "LOG-42", "--at", "2026-09-05", "--note", "Rolled forward.")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	body := map[string]any{}
	_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body)
	if body["version"] != "1.4.0" || body["ticket"] != "LOG-42" || body["note"] != "Rolled forward." {
		t.Errorf("body = %v", body)
	}
	// `at` is what makes backfilling possible, and the contract wants RFC 3339.
	if body["at"] != "2026-09-05T00:00:00Z" {
		t.Errorf("at = %v", body["at"])
	}

	// Nothing but the version: what was not given is not sent.
	if code, _, _ = run(t, server, "deploy", "logaffe-prod", "--version", "1.4.0"); code != exit.OK {
		t.Fatal("code")
	}
	body = map[string]any{}
	_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body)
	if len(body) != 1 || body["version"] != "1.4.0" {
		t.Errorf("body = %v", body)
	}
}

// `set` is narrow, and `--version` is here to be refused by the instance: the
// answer says the rule, which an unknown flag would not.
func TestCorrectingAVersionIsTheInstancesRefusal(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 400, `{"type":"/problems/unknown-field","title":"unknown-field","status":400,
		"detail":"A deployment's version is what the record is; a wrong one is deleted and recorded again."}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "deploy", "set", "logaffe-prod", "3", "--version", "1.5.0")
	if code != exit.Refused {
		t.Fatalf("code %d", code)
	}
	if out != "" || !strings.Contains(stderr, "deleted and recorded again") {
		t.Fatalf("stdout %q, stderr %q", out, stderr)
	}

	body := map[string]any{}
	_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body)
	if body["version"] != "1.5.0" {
		t.Errorf("the attempt reaches the instance, which is what says why: %v", body)
	}
}

func TestDeploymentUsageMistakesAreExitTwo(t *testing.T) {
	f := deployments()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, args := range [][]string{
		{"deploy", "list"},
		{"deploy", "set", "logaffe-prod", "3"},
		{"deploy", "view", "logaffe-prod", "three"},
		{"deploy", "view", "logaffe-prod", "0"},
		{"deploy", "logaffe-prod", "--version", "1.4.0", "--at", "last tuesday"},
		{"deploy", "logaffe-prod", "--version", "1.4.0", "--note", "one", "--note-file", "-"},
	} {
		if code, _, stderr := run(t, server, args...); code != exit.Usage || stderr == "" {
			t.Errorf("%v: code %d, stderr %q", args, code, stderr)
		}
	}
}

// The view says what a deployment is: the version it put there, the one before
// it, and the file revisions that were current when it went live.
func TestDeploymentViewPrintsWhatWasDerivedByAt(t *testing.T) {
	f := deployments()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "deploy", "view", "logaffe-prod", "3")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	for _, want := range []string{
		"logaffe-prod #3  1.4.0\n",
		"at: 2026-09-05T12:00:00Z by maintainer\n",
		"previous: 1.3.2  ticket: LOG-42\n",
		"files: compose.yml@4, sites/logaffe.caddy@2\n",
		"recorded: 2026-09-05T12:00:01Z  corrected: 2026-09-06T08:00:00Z by quiet-otter-42\n",
	} {
		if !strings.Contains(out, want) {
			t.Errorf("stdout lacks %q:\n%s", want, out)
		}
	}
	if !strings.HasSuffix(out, "\nRolled forward after the schema migration.\n") {
		t.Errorf("the note is printed as it is stored:\n%q", out)
	}
}

// One that was never corrected says nothing about corrections.
func TestADeploymentNeverCorrectedSaysSo(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `{"installation":"logaffe-prod","number":1,"version":"1.0.0","previous":null,"ref":null,
		"files":[],"at":"2026-09-05T12:00:00Z",
		"by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
		"ticket":null,"note":"","created_at":"2026-09-05T12:00:00Z",
		"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
		"updated_at":"2026-09-05T12:00:00Z"}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "deploy", "view", "logaffe-prod", "1")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	want := "logaffe-prod #1  1.0.0\nat: 2026-09-05T12:00:00Z by maintainer\n" +
		"recorded: 2026-09-05T12:00:00Z\n"
	if out != want {
		t.Fatalf("the first deployment of an installation has no previous and no files:\n%q", out)
	}
}
