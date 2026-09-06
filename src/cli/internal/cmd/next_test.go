package cmd

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

// fake is an instance as far as `ha next` needs one: it records what it was
// asked and answers what the test says.
type fake struct {
	t        *testing.T
	version  string
	requests []*http.Request
	bodies   []string
	answer   func(r *http.Request) (int, string)
	headers  func(r *http.Request) map[string]string
	// What a success is served as, where the test is about an answer that is
	// not JSON; empty means the ordinary application/json.
	contentType string
}

func (f *fake) handler() http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := readAll(r)
		f.requests = append(f.requests, r)
		f.bodies = append(f.bodies, body)
		status, reply := f.answer(r)
		w.Header().Set("Hostingaffe-Version", f.version)
		if f.headers != nil {
			for name, value := range f.headers(r) {
				w.Header().Set(name, value)
			}
		}
		switch {
		case status >= 400:
			w.Header().Set("Content-Type", "application/problem+json")
		case f.contentType != "":
			w.Header().Set("Content-Type", f.contentType)
		default:
			w.Header().Set("Content-Type", "application/json")
		}
		w.WriteHeader(status)
		_, _ = w.Write([]byte(reply))
	})
}

func readAll(r *http.Request) (string, error) {
	var buf bytes.Buffer
	_, err := buf.ReadFrom(r.Body)
	return buf.String(), err
}

func run(t *testing.T, server *httptest.Server, dir string, args ...string) (code int, stdout, stderr string) {
	t.Helper()
	var out, errOut bytes.Buffer
	code = Run(context.Background(), args, Env{
		Getenv: func(k string) string {
			return map[string]string{"HOSTINGAFFE_URL": server.URL, "HOSTINGAFFE_TOKEN": "ha_test-token-of-thirty-two-characters-or-more"}[k]
		},
		Dir:    dir,
		Stdin:  strings.NewReader(""),
		Stdout: &out,
		Stderr: &errOut,
		HTTP:   server.Client(),
	})
	return code, out.String(), errOut.String()
}

func repository(t *testing.T, content string) string {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, ".hostingaffe"), []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
	return dir
}

const issue = `{"key":"PLAN-42","project":"PLAN","title":"Settle the claim","description":"Four columns.","result":null,"status":"in_progress","ready":true,"priority":3,
"labels":[{"name":"feature","group":"kind","description":null}],"epic":{"key":"PLAN-E2","title":"Backend","description":"The plan.","status":"open"},"assignee":null,
"claim":{"holder":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},"since":"2026-09-02T14:03:07.123456Z","expires_at":"2026-09-02T18:03:07.123456Z"},
"author":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"blocked_by":[{"key":"PLAN-40","title":"The schema","status":"done","open":false,"result":"Four tables, and the view derives the status."}],"blocks":[],
"open_questions":0,"open_blockers":0,"open_sub_issues":0,"comments":[],"questions":[],
"project_context":{"key":"PLAN","name":"hostingaffe","triage_required":false,"review_required":false,"labels":[],
"instructions":{"slug":"agents","title":"How work runs here","body":"Tests run here with just test, and no dependency lands without asking."}},
"created_at":"2026-09-02T14:00:00.000000Z","updated_at":"2026-09-02T14:03:07.123456Z","closed_at":null}`

const reasons = `{"blocked":3,"waiting_for_answer":2,"in_progress":4,"in_review":5,"parked":6,"not_ready":1,"assigned_elsewhere":0}`

func TestNextClaimPrintsTheIssueAndSendsWhatEveryWriteCarries(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method != http.MethodPost || r.URL.Path != "/projects/PLAN/next" {
			return 404, `{"type":"/problems/not-found","status":404}`
		}
		return 200, `{"issue":` + issue + `,"reasons":` + reasons + `}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, repository(t, "project = PLAN\nrepo = repo/api\n"), "next", "--claim", "--wait", "60", "--ready", "--label", "feature", "--label", "cut-1")

	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
	if !strings.Contains(out, "PLAN-42  Settle the claim") || !strings.Contains(out, "claimed by: quiet-otter-42") || !strings.Contains(out, "The plan.") || !strings.Contains(out, "Four columns.") {
		t.Fatalf("unexpected output:\n%s", out)
	}

	// The context package in one pass (VISION 15.5): the project's
	// instructions, the epic's document, the ticket, and what its blocker
	// decided — in that order, widest first, and none of it as a reference to
	// fetch afterwards.
	for _, want := range []string{
		"## How work runs here (PLAN/agents)",
		"Tests run here with just test, and no dependency lands without asking.",
		"## What PLAN-40 decided  The schema",
		"Four tables, and the view derives the status.",
	} {
		if !strings.Contains(out, want) {
			t.Errorf("output lacks %q:\n%s", want, out)
		}
	}
	if strings.Index(out, "How work runs here") > strings.Index(out, "The plan.") {
		t.Errorf("the project's instructions belong above the epic's document:\n%s", out)
	}

	req := f.requests[0]
	if got := req.Header.Get("Authorization"); got != "Bearer ha_test-token-of-thirty-two-characters-or-more" {
		t.Errorf("Authorization = %q", got)
	}
	if got := req.Header.Get("User-Agent"); !strings.HasPrefix(got, "ha/0.0.0-dev (") {
		t.Errorf("User-Agent = %q", got)
	}
	if got := req.Header.Get("Idempotency-Key"); len(got) != 34 || !strings.HasSuffix(got, "-1") {
		t.Errorf("Idempotency-Key = %q; expected one ha generated, numbered per write", got)
	}

	var body map[string]any
	if err := json.Unmarshal([]byte(f.bodies[0]), &body); err != nil {
		t.Fatal(err)
	}
	if body["ready"] != true || body["repo"] != "repo/api" {
		t.Errorf("body = %v", body)
	}
	if body["wait"] != float64(60) {
		t.Errorf("wait = %v", body["wait"])
	}
	if labels, _ := body["label"].([]any); len(labels) != 2 || labels[0] != "feature" || labels[1] != "cut-1" {
		t.Errorf("labels = %v", body["label"])
	}
}

func TestNextClaimWithNothingWorkableExitsEightWithTheReasons(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `{"issue":null,"reasons":` + reasons + `}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, repository(t, "project = PLAN\n"), "next", "--claim", "--repo", "none")
	if code != exit.Empty {
		t.Fatalf("code %d, want %d; stderr %q", code, exit.Empty, errOut)
	}
	if !strings.Contains(out, "3 blocked, 2 waiting for an answer, 4 in progress, 5 in review, 6 parked, 1 not ready, 0 assigned elsewhere") {
		t.Fatalf("unexpected output:\n%s", out)
	}
	if errOut != "" {
		t.Fatalf("stderr should be empty, got %q", errOut)
	}
	if strings.Contains(f.bodies[0], "repo") && !strings.Contains(f.bodies[0], `"repo":null`) {
		t.Fatalf("--repo none should send no repo, body %s", f.bodies[0])
	}

	// --json prints the answer as it came, issue null and all.
	code, out, _ = run(t, server, repository(t, "project = PLAN\n"), "next", "--claim", "--json")
	if code != exit.Empty || !strings.Contains(out, `"issue": null`) {
		t.Fatalf("code %d, out %s", code, out)
	}
}

func TestWaitLongerThanTheServerLimitUsesRounds(t *testing.T) {
	calls := 0
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		calls++
		if calls == 1 {
			return 200, `{"issue":null,"reasons":` + reasons + `}`
		}
		return 200, `{"issue":` + issue + `,"reasons":` + reasons + `}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, repository(t, "project = PLAN\n"), "next", "--claim", "--wait", "3601")
	if code != exit.OK || errOut != "" || !strings.Contains(out, "PLAN-42") {
		t.Fatalf("code %d, stdout %q, stderr %q", code, out, errOut)
	}
	if len(f.bodies) != 2 {
		t.Fatalf("requests = %d, want 2", len(f.bodies))
	}
	for i, want := range []float64{3600, 1} {
		var body map[string]any
		if err := json.Unmarshal([]byte(f.bodies[i]), &body); err != nil {
			t.Fatal(err)
		}
		if body["wait"] != want {
			t.Errorf("round %d wait = %v, want %.0f", i+1, body["wait"], want)
		}
	}
}

func TestNextWithoutClaimListsAndSendsLabelsAsQuery(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method != http.MethodGet {
			return 405, ""
		}
		return 200, `{"items":[{"key":"PLAN-1","project":"PLAN","title":"A","status":"todo","ready":true,"priority":4,"labels":["bug"],"epic":null,"assignee":null,"claim":null,"blocked_by":[],"open_questions":0,"open_blockers":0,"open_sub_issues":0,"created_at":"2026-09-02T14:00:00.000000Z","updated_at":"2026-09-02T14:00:00.000000Z","closed_at":null,"deleted_at":null,"deleted_by":null}],"total":1,"has_more":false,"reasons":` + reasons + `}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, t.TempDir(), "next", "--project", "plan", "--label", "bug", "--epic", "PLAN-E2")
	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
	if !strings.Contains(out, "PLAN-1") || !strings.Contains(out, "P4") {
		t.Fatalf("unexpected output:\n%s", out)
	}
	q := f.requests[0].URL.Query()
	if q.Get("label") != "bug" || q.Get("epic") != "PLAN-E2" || f.requests[0].URL.Path != "/projects/plan/next" {
		t.Fatalf("unexpected request %s", f.requests[0].URL)
	}
	if f.requests[0].Header.Get("Idempotency-Key") != "" {
		t.Fatal("a read carries no idempotency key")
	}
}

func TestExitCodesFollowTheProblemDocument(t *testing.T) {
	cases := []struct {
		status int
		body   string
		want   int
		text   string
	}{
		{404, `{"type":"/problems/not-found","title":"Nothing by that key","status":404,"detail":"No project PLAN."}`, exit.NotFound, "No project PLAN. (not-found)"},
		{422, `{"type":"/problems/unknown-label","title":"x","status":422,"detail":"Project PLAN has no label repo/x in the group repo."}`, exit.Refused, "unknown-label"},
		{401, `{"type":"/problems/unauthenticated","title":"No token","status":401}`, exit.Denied, "No token (unauthenticated)"},
		{409, `{"type":"/problems/claim-held","title":"held","status":409,"detail":"Held by one."}`, exit.Conflict, "claim-held"},
		{500, `{"type":"/problems/internal","title":"Something went wrong on the server","status":500}`, exit.Unexpected, "internal"},
		{502, ``, exit.Unexpected, "502"},
	}
	for _, c := range cases {
		f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return c.status, c.body }}
		server := httptest.NewServer(f.handler())
		code, out, errOut := run(t, server, repository(t, "project = PLAN\n"), "next", "--claim")
		server.Close()
		if code != c.want || out != "" || !strings.Contains(errOut, c.text) {
			t.Errorf("status %d: code %d (want %d), stdout %q, stderr %q", c.status, code, c.want, out, errOut)
		}
	}
}

func TestSkewAndUnreachableHaveTheirOwnCodes(t *testing.T) {
	f := &fake{t: t, version: "9.0.0", answer: func(*http.Request) (int, string) { return 200, `{"issue":null,"reasons":` + reasons + `}` }}
	server := httptest.NewServer(f.handler())
	code, _, errOut := run(t, server, repository(t, "project = PLAN\n"), "next", "--claim")
	server.Close()
	// A development build is never skewed; a released one against 9.0.0 is.
	if code != exit.Empty {
		t.Fatalf("a dev build is not checked for skew; code %d, stderr %q", code, errOut)
	}

	code, _, errOut = run(t, server, repository(t, "project = PLAN\n"), "next", "--claim")
	if code != exit.Unreachable || !strings.Contains(errOut, "could not be reached") {
		t.Fatalf("a closed server is unreachable; code %d, stderr %q", code, errOut)
	}
}

func TestUsageMistakesAreExitTwo(t *testing.T) {
	server := httptest.NewServer(http.NotFoundHandler())
	defer server.Close()

	code, _, errOut := run(t, server, t.TempDir(), "next", "--claim")
	if code != exit.Usage || !strings.Contains(errOut, "no project") {
		t.Fatalf("no project: code %d, stderr %q", code, errOut)
	}

	code, _, errOut = run(t, server, t.TempDir(), "next", "--nope")
	if code != exit.Usage || !strings.Contains(errOut, "unknown flag") {
		t.Fatalf("unknown flag: code %d, stderr %q", code, errOut)
	}

	code, _, errOut = run(t, server, repository(t, "project = PLAN\n"), "next", "--wait", "60")
	if code != exit.Usage || !strings.Contains(errOut, "requires --claim") {
		t.Fatalf("wait without claim: code %d, stderr %q", code, errOut)
	}

	code, _, errOut = run(t, server, repository(t, "project = PLAN\n"), "next", "--claim", "--wait", "0")
	if code != exit.Usage || !strings.Contains(errOut, "positive") {
		t.Fatalf("zero wait: code %d, stderr %q", code, errOut)
	}

	var out bytes.Buffer
	code = Run(context.Background(), []string{"next"}, Env{Getenv: func(string) string { return "" }, Dir: t.TempDir(), Stdin: strings.NewReader(""), Stdout: &out, Stderr: &out})
	if code != exit.Usage || !strings.Contains(out.String(), "HOSTINGAFFE_URL") {
		t.Fatalf("missing variables: code %d, output %q", code, out.String())
	}
}
