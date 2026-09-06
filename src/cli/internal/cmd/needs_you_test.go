package cmd

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

func TestNeedsYouPrintsGroupsAndPages(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method != http.MethodGet || r.URL.Path != "/projects/PLAN/needs-you" {
			return 404, `{"type":"/problems/not-found","status":404}`
		}
		return 200, `{"items":[` +
			`{"because":"question","issue":{"key":"PLAN-1","project":"PLAN","title":"Answer me","status":"todo","ready":true,"priority":4,"labels":[],"epic":null,"parent":null,"assignee":null,"claim":null,"blocked_by":[],"open_questions":1,"open_blockers":0,"open_sub_issues":0,"created_at":"2026-09-02T14:00:00Z","updated_at":"2026-09-02T14:00:00Z","closed_at":null,"deleted_at":null,"deleted_by":null}},` +
			`{"because":"review","issue":{"key":"PLAN-2","project":"PLAN","title":"Check me","status":"review","ready":true,"priority":3,"labels":[],"epic":null,"parent":null,"assignee":null,"claim":null,"blocked_by":[],"open_questions":0,"open_blockers":0,"open_sub_issues":0,"created_at":"2026-09-02T14:00:00Z","updated_at":"2026-09-02T14:00:00Z","closed_at":null,"deleted_at":null,"deleted_by":null}}` +
			`],"total":3,"has_more":true,"next_cursor":"next-page","agents":1}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, repository(t, "project = PLAN\n"), "needs-you", "--limit", "2", "--cursor", "previous")
	if code != exit.OK {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
	if !strings.Contains(out, "Open questions:\nPLAN-1") || !strings.Contains(out, "In review:\nPLAN-2") {
		t.Fatalf("unexpected output:\n%s", out)
	}
	if !strings.Contains(errOut, "2 of 3; next page: --cursor next-page") {
		t.Fatalf("unexpected stderr %q", errOut)
	}
	query := f.requests[0].URL.Query()
	if query.Get("limit") != "2" || query.Get("cursor") != "previous" {
		t.Fatalf("unexpected query %s", f.requests[0].URL.RawQuery)
	}
	if f.requests[0].Header.Get("Idempotency-Key") != "" {
		t.Fatal("a read carries no idempotency key")
	}
}

// PLAN-29: without an agent token the list used to call every blocked issue
// stuck at once. It is a fact about the instance, so it is said once, beside a
// list that is now empty.
func TestNeedsYouSaysTheInstanceHasNoAgentBesideTheList(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `{"items":[],"total":0,"has_more":false,"next_cursor":null,"agents":0}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, repository(t, "project = PLAN\n"), "needs-you")
	if code != exit.OK {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
	if !strings.Contains(out, "Nothing needs you.") {
		t.Errorf("an empty list explains itself; stdout %q", out)
	}
	if !strings.Contains(errOut, "no active agent token") || !strings.Contains(errOut, "ha agent create") {
		t.Errorf("stderr %q says nothing about the missing agent", errOut)
	}
}

func TestNeedsYouJSONKeepsThePageShape(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `{"items":[],"total":0,"has_more":false,"next_cursor":null,"agents":1}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, repository(t, "project = PLAN\n"), "needs-you", "--json")
	if code != exit.OK || errOut != "" || !strings.Contains(out, `"items": []`) || !strings.Contains(out, `"total": 0`) {
		t.Fatalf("code %d, stdout %q, stderr %q", code, out, errOut)
	}
}

func TestNeedsYouWaitLongPollsWithThePreviousETag(t *testing.T) {
	calls := 0
	f := &fake{t: t, version: "0.0.0-dev"}
	f.answer = func(r *http.Request) (int, string) {
		calls++
		if calls == 1 {
			return 200, `{"items":[],"total":0,"has_more":false,"next_cursor":null,"agents":1}`
		}
		return 200, `{"items":[{"because":"question","issue":{"key":"PLAN-1","project":"PLAN","title":"Answer me","status":"todo","ready":true,"priority":4,"labels":[],"epic":null,"parent":null,"assignee":null,"claim":null,"blocked_by":[],"open_questions":1,"open_blockers":0,"open_sub_issues":0,"created_at":"2026-09-02T14:00:00Z","updated_at":"2026-09-02T14:00:00Z","closed_at":null,"deleted_at":null,"deleted_by":null}}],"total":1,"has_more":false,"next_cursor":null,"agents":1}`
	}
	f.headers = func(*http.Request) map[string]string {
		if calls == 1 {
			return map[string]string{"ETag": `"empty"`}
		}
		return map[string]string{"ETag": `"question"`}
	}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, repository(t, "project = PLAN\n"), "needs-you", "--wait", "60")
	if code != exit.OK || errOut != "" || !strings.Contains(out, "PLAN-1") {
		t.Fatalf("code %d, stdout %q, stderr %q", code, out, errOut)
	}
	if len(f.requests) != 2 || f.requests[1].Header.Get("If-None-Match") != `"empty"` || f.requests[1].URL.Query().Get("wait") != "60" {
		t.Fatalf("requests = %d, second headers/query = %v %v", len(f.requests), f.requests[1].Header, f.requests[1].URL.Query())
	}
}

func TestNeedsYouWaitDeadlineIsEmptyAndBadWaitIsUsage(t *testing.T) {
	f := &fake{
		t: t, version: "0.0.0-dev",
		answer: func(r *http.Request) (int, string) {
			if r.Header.Get("If-None-Match") != "" {
				return http.StatusNotModified, ""
			}
			return 200, `{"items":[],"total":0,"has_more":false,"next_cursor":null,"agents":1}`
		},
		headers: func(*http.Request) map[string]string { return map[string]string{"ETag": `"empty"`} },
	}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := repository(t, "project = PLAN\n")

	code, out, errOut := run(t, server, dir, "needs-you", "--wait", "1", "--json")
	if code != exit.Empty || errOut != "" || !strings.Contains(out, `"items": []`) {
		t.Fatalf("code %d, stdout %q, stderr %q", code, out, errOut)
	}
	code, _, errOut = run(t, server, dir, "needs-you", "--wait", "0")
	if code != exit.Usage || !strings.Contains(errOut, "positive") {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
}
