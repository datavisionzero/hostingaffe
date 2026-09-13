package cmd

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

// The secret goes to stdout and the sentence about it to stderr, so that what
// is piped into a file on the host is the token and nothing else (ADR 0016).
func TestMachineTokenIssuePrintsTheSecretOnceAndTheWordBeside(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method != http.MethodPost || r.URL.Path != "/api/machines/ex44/token" {
			t.Fatalf("asked %s %s", r.Method, r.URL.Path)
		}
		if r.URL.Query().Get("rotate") != "" {
			t.Fatalf("rotate was sent without the flag: %q", r.URL.RawQuery)
		}
		return 201, `{"machine":"ex44","prefix":"ha_abcde","secret":"ha_the-secret-of-this-machine-and-no-other","issued_at":"2026-09-13T08:00:00Z"}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := run(t, server, "machine", "token", "issue", "ex44")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	if !strings.Contains(stdout, "ha_the-secret-of-this-machine-and-no-other") {
		t.Fatalf("the secret is not on stdout: %q", stdout)
	}
	if strings.Contains(stderr, "ha_the-secret") {
		t.Fatalf("the secret reached stderr: %q", stderr)
	}
	if !strings.Contains(stderr, "shown once") {
		t.Fatalf("nothing said about the one showing: %q", stderr)
	}
}

func TestMachineTokenIssueSendsRotateOnlyWhenAsked(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Query().Get("rotate") != "true" {
			t.Fatalf("rotate was not sent: %q", r.URL.RawQuery)
		}
		return 201, `{"machine":"ex44","prefix":"ha_abcde","secret":"ha_second-secret-of-thirty-two-characters","issued_at":"2026-09-13T08:00:00Z"}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	if code, _, stderr := run(t, server, "machine", "token", "issue", "ex44", "--rotate"); code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
}

// `show` is what somebody reads when a machine has gone quiet, and it never
// shows a secret, because only the hash is kept.
func TestMachineTokenShowSaysWhenItWasLastUsed(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method != http.MethodGet || r.URL.Path != "/api/machines/ex44/token" {
			t.Fatalf("asked %s %s", r.Method, r.URL.Path)
		}
		return 200, `{"present":true,"prefix":"ha_abcde","issued_by":{"id":"01a09b9b-7d0b-7f69-b036-4e70c668fedc","kind":"user","name":"maintainer"},"issued_at":"2026-09-13T08:00:00Z","last_used_at":"2026-09-13T08:15:00Z","revoked_by":null,"revoked_at":null}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := run(t, server, "machine", "token", "show", "ex44")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	for _, want := range []string{"ex44", "ha_abcde", "maintainer", "last used"} {
		if !strings.Contains(stdout, want) {
			t.Fatalf("%q is not in the output: %q", want, stdout)
		}
	}
}

func TestMachineTokenShowSaysWhenThereIsNone(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `{"present":false,"prefix":null,"issued_by":null,"issued_at":null,"last_used_at":null,"revoked_by":null,"revoked_at":null}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, _ := run(t, server, "machine", "token", "show", "ex44")
	if code != 0 || !strings.Contains(stdout, "has no token") {
		t.Fatalf("exit %d, output %q", code, stdout)
	}
}

func TestMachineTokenRevokeSaysWhatItTookBack(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method != http.MethodDelete || r.URL.Path != "/api/machines/ex44/token" {
			t.Fatalf("asked %s %s", r.Method, r.URL.Path)
		}
		return 204, ""
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := run(t, server, "machine", "token", "revoke", "ex44")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	if !strings.Contains(stdout, "revoked") {
		t.Fatalf("nothing said about the revocation: %q", stdout)
	}
}

// An agent administers no keys, and the instance says so: ha passes the refusal
// through as exit 7 (docs/cli.md, Exit codes).
func TestMachineTokenIssueUnderAnAgentIsDenied(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 403, `{"type":"/problems/forbidden","title":"The identity may not do this","status":403,"detail":"Only a user may issue a machine's token; an agent may not (ADR 0015)."}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := run(t, server, "machine", "token", "issue", "ex44")
	if code != 7 {
		t.Fatalf("exit %d, stderr %q", code, stderr)
	}
}
