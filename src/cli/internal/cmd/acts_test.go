package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const question = `{"id":"0198e0c0-0000-7000-8000-00000000000a","question":"Which Postgres?","asked_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},"asked_at":"2026-09-02T14:03:07.000000Z","answer":null,"answered_by":null,"answered_at":null}`

func TestTheActsHitTheirEndpointsWithTheirBodies(t *testing.T) {
	inReview := strings.Replace(issue, `"status":"in_progress"`, `"status":"review"`, 1)
	withResult := strings.Replace(issue, `"result":null`, `"result":"Shipped."`, 1)
	var f *fake
	f = &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.URL.Path == "/issues/PLAN-42/close" && strings.Contains(f.bodies[len(f.bodies)-1], "Shipped."):
			return 200, withResult
		case r.URL.Path == "/issues/PLAN-42/comments", r.URL.Path == "/issues/PLAN-42/questions":
			return 201, question
		case r.URL.Path == "/questions/0198e0c0-0000-7000-8000-00000000000a/answer":
			return 200, strings.Replace(question, `"answer":null`, `"answer":"18."`, 1)
		case r.Method == http.MethodGet && r.URL.Path == "/issues/PLAN-42":
			return 200, inReview
		default:
			return 200, issue
		}
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := t.TempDir()

	cases := []struct {
		args   []string
		stdin  string
		method string
		path   string
		body   map[string]any
		stderr string
	}{
		{[]string{"issue", "claim", "PLAN-42", "--force"}, "", "POST", "/issues/PLAN-42/claim", map[string]any{"force": true}, ""},
		{[]string{"issue", "release", "PLAN-42"}, "", "POST", "/issues/PLAN-42/release", nil, ""},
		{[]string{"issue", "close", "PLAN-42", "--done", "--result-file", "-"}, "Shipped.\n", "POST", "/issues/PLAN-42/close", map[string]any{"status": "done", "result": "Shipped."}, ""},
		{[]string{"issue", "close", "PLAN-42", "--canceled"}, "", "POST", "/issues/PLAN-42/close", map[string]any{"status": "canceled", "result": nil}, "closed without a result"},
		{[]string{"issue", "review", "PLAN-42"}, "", "POST", "/issues/PLAN-42/review", map[string]any{"result": nil}, "handed in without a result"},
		{[]string{"issue", "reopen", "PLAN-42", "--comment", "Not quite."}, "", "POST", "/issues/PLAN-42/reopen", map[string]any{"comment": "Not quite."}, ""},
		{[]string{"issue", "reopen", "PLAN-42"}, "", "POST", "/issues/PLAN-42/reopen", map[string]any{"comment": nil}, "sent back from review without a comment"},
		{[]string{"issue", "park", "PLAN-42"}, "", "PATCH", "/issues/PLAN-42", map[string]any{"status": "backlog"}, ""},
		{[]string{"issue", "unpark", "PLAN-42"}, "", "PATCH", "/issues/PLAN-42", map[string]any{"status": "todo"}, ""},
		{[]string{"issue", "comment", "PLAN-42", "Halfway."}, "", "POST", "/issues/PLAN-42/comments", map[string]any{"body": "Halfway."}, ""},
		{[]string{"issue", "ask", "PLAN-42", "--file", "-"}, "Which Postgres?", "POST", "/issues/PLAN-42/questions", map[string]any{"question": "Which Postgres?"}, ""},
		{[]string{"question", "answer", "0198e0c0-0000-7000-8000-00000000000a", "18."}, "", "POST", "/questions/0198e0c0-0000-7000-8000-00000000000a/answer", map[string]any{"answer": "18."}, ""},
	}

	for _, c := range cases {
		var out, errOut strings.Builder
		code := Run(t.Context(), c.args, Env{
			Getenv: func(k string) string {
				return map[string]string{"HOSTINGAFFE_URL": server.URL, "HOSTINGAFFE_TOKEN": "ha_test-token-of-thirty-two-characters-or-more"}[k]
			},
			Dir: dir, Stdin: strings.NewReader(c.stdin), Stdout: &out, Stderr: &errOut, HTTP: server.Client(),
		})
		if code != exit.OK {
			t.Fatalf("%v: code %d, stderr %s", c.args, code, errOut.String())
		}

		last := f.requests[len(f.requests)-1]
		if last.Method != c.method || last.URL.Path != c.path {
			t.Errorf("%v: %s %s, want %s %s", c.args, last.Method, last.URL.Path, c.method, c.path)
		}
		if c.body != nil {
			var body map[string]any
			_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body)
			for k, want := range c.body {
				if got := body[k]; got != want {
					t.Errorf("%v: body[%s] = %v, want %v", c.args, k, got, want)
				}
			}
		}
		if last.Header.Get("Idempotency-Key") == "" {
			t.Errorf("%v: a write carries a key", c.args)
		}
		if c.stderr == "" && errOut.Len() > 0 {
			t.Errorf("%v: unexpected stderr %q", c.args, errOut.String())
		}
		if c.stderr != "" && !strings.Contains(errOut.String(), c.stderr) {
			t.Errorf("%v: stderr %q, want %q", c.args, errOut.String(), c.stderr)
		}
	}

	code, _, errOut := run(t, server, dir, "issue", "close", "PLAN-42")
	if code != exit.Usage || !strings.Contains(errOut, "--done or --canceled") {
		t.Errorf("close without a status: code %d, stderr %q", code, errOut)
	}
	code, _, errOut = run(t, server, dir, "question", "answer", "not-an-id", "x")
	if code != exit.Usage || !strings.Contains(errOut, "not a question id") {
		t.Errorf("bad id: code %d, stderr %q", code, errOut)
	}
}

func TestQuestionListDefaultsToOpenAndTheProject(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `{"items":[{"id":"0198e0c0-0000-7000-8000-00000000000a","issue":{"key":"PLAN-42","title":"Settle the claim"},"question":"Which Postgres?","asked_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},"asked_at":"2026-09-02T14:03:07.000000Z","answer":null,"answered_by":null,"answered_at":null}],"total":1,"has_more":false,"next_cursor":null}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, _ := run(t, server, repository(t, "project = PLAN\n"), "question", "list")
	if code != exit.OK || !strings.Contains(out, "PLAN-42") || !strings.Contains(out, "Which Postgres?") || !strings.Contains(out, "open") {
		t.Fatalf("code %d, out %s", code, out)
	}
	q := f.requests[0].URL.Query()
	if q.Get("project") != "PLAN" || q.Get("open") != "true" {
		t.Errorf("query %v", q)
	}

	_, _, _ = run(t, server, repository(t, "project = PLAN\n"), "question", "list", "--answered", "--issue", "PLAN-42", "-q", "serializable mode")
	q = f.requests[1].URL.Query()
	if q.Get("open") != "false" || q.Get("issue") != "PLAN-42" || q.Get("q") != "serializable mode" {
		t.Errorf("query %v", q)
	}
	_, _, _ = run(t, server, repository(t, "project = PLAN\n"), "question", "list", "--all")
	if f.requests[2].URL.Query().Has("open") {
		t.Errorf("--all sends no open: %v", f.requests[2].URL.Query())
	}
}

func TestIssueAskWaitsForTheAnswerAndObservesItsClaimDeadline(t *testing.T) {
	futureIssue := strings.Replace(issue, "2026-09-02T18:03:07.123456Z", "2099-09-02T18:03:07.123456Z", 1)
	answered := strings.Replace(question, `"answer":null`, `"answer":"Use 18."`, 1)
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.Method == http.MethodPost && r.URL.Path == "/issues/PLAN-42/questions":
			return 201, question
		case r.Method == http.MethodGet && r.URL.Path == "/issues/PLAN-42":
			return 200, futureIssue
		case r.Method == http.MethodGet && r.URL.Path == "/questions/0198e0c0-0000-7000-8000-00000000000a":
			return 200, answered
		default:
			return 404, `{"type":"/problems/not-found","status":404}`
		}
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, t.TempDir(), "issue", "ask", "PLAN-42", "Which Postgres?", "--wait", "60")
	if code != exit.OK || errOut != "" || !strings.Contains(out, "answered on PLAN-42: Use 18.") {
		t.Fatalf("code %d, stdout %q, stderr %q", code, out, errOut)
	}
	if len(f.requests) != 3 || f.requests[2].URL.Query().Get("wait") != "60" {
		t.Fatalf("requests = %d, final query %v", len(f.requests), f.requests[2].URL.Query())
	}
	if f.requests[1].Header.Get("Idempotency-Key") != "" || f.requests[2].Header.Get("Idempotency-Key") != "" {
		t.Fatal("the reads after asking must not carry idempotency keys")
	}

	code, _, errOut = run(t, server, t.TempDir(), "issue", "ask", "PLAN-42", "x", "--wait", "0")
	if code != exit.Usage || !strings.Contains(errOut, "positive") {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
}

func TestIssueAskDoesNotWaitPastAnExpiredOwnClaim(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method == http.MethodPost {
			return 201, question
		}
		return 200, issue
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, t.TempDir(), "issue", "ask", "PLAN-42", "Which Postgres?", "--wait", "60")
	if code != exit.Empty || errOut != "" || !strings.Contains(out, "asked on PLAN-42") {
		t.Fatalf("code %d, stdout %q, stderr %q", code, out, errOut)
	}
	if len(f.requests) != 2 {
		t.Fatalf("requests = %d; no question read should start past claim expiry", len(f.requests))
	}
}
