package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const created = `{"items":[` + issue + `]}`

func TestIssueCreateSendsOneIssueWithTheRepoLabelAndTheDescriptionFromStdin(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) { return 201, created }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := repository(t, "project = PLAN\nrepo = repo/api\n")
	env := Env{
		Getenv: func(k string) string {
			return map[string]string{"HOSTINGAFFE_URL": server.URL, "HOSTINGAFFE_TOKEN": "ha_test-token-of-thirty-two-characters-or-more"}[k]
		},
		Dir: dir, Stdin: strings.NewReader("# The plan\n\nDo it.\n"), Stdout: new(strings.Builder), Stderr: new(strings.Builder), HTTP: server.Client(),
	}
	code := Run(t.Context(), []string{"issue", "create", "Settle the claim", "--description-file", "-", "--priority", "3", "--ready", "--label", "feature", "--epic", "PLAN-E2", "--blocked-by", "PLAN-40", "--backlog"}, env)
	if code != exit.OK {
		t.Fatalf("code %d, stderr %s", code, env.Stderr.(*strings.Builder).String())
	}

	var body map[string]any
	if err := json.Unmarshal([]byte(f.bodies[0]), &body); err != nil {
		t.Fatal(err)
	}
	if body["project"] != "PLAN" {
		t.Errorf("project = %v", body["project"])
	}
	items := body["issues"].([]any)
	item := items[0].(map[string]any)
	if item["title"] != "Settle the claim" || item["description"] != "# The plan\n\nDo it." || item["priority"] != float64(3) || item["ready"] != true || item["epic"] != "PLAN-E2" || item["status"] != "backlog" {
		t.Errorf("item = %v", item)
	}
	labels := item["labels"].([]any)
	if len(labels) != 2 || labels[0] != "feature" || labels[1] != "repo/api" {
		t.Errorf("labels = %v; the repo label of the file is added", labels)
	}
	if blocked := item["blocked_by"].([]any); len(blocked) != 1 || blocked[0] != "PLAN-40" {
		t.Errorf("blocked_by = %v", item["blocked_by"])
	}
	if !strings.Contains(env.Stdout.(*strings.Builder).String(), "PLAN-42  Settle the claim") {
		t.Errorf("stdout: %s", env.Stdout.(*strings.Builder).String())
	}
}

func TestIssueCreateFromAFileKeepsRefsAndBlockersAndFillsTheProject(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) { return 201, `{"items":[` + issue + `,` + issue + `]}` }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	dir := repository(t, "project = PLAN\n")
	batch := filepath.Join(dir, "batch.json")
	if err := os.WriteFile(batch, []byte(`{"issues":[{"ref":"schema","title":"The schema","priority":3},{"ref":"contract","title":"The contract","blocked_by":["schema","PLAN-6"]}]}`), 0o644); err != nil {
		t.Fatal(err)
	}

	code, out, errOut := run(t, server, dir, "issue", "create", "--file", batch, "--repo", "none", "--json")
	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}

	var body map[string]any
	_ = json.Unmarshal([]byte(f.bodies[0]), &body)
	if body["project"] != "PLAN" {
		t.Errorf("project filled from the file: %v", body["project"])
	}
	items := body["issues"].([]any)
	second := items[1].(map[string]any)
	if second["ref"] != "contract" {
		t.Errorf("ref lost: %v", second)
	}
	if blocked := second["blocked_by"].([]any); len(blocked) != 2 || blocked[0] != "schema" || blocked[1] != "PLAN-6" {
		t.Errorf("blockers lost: %v", second["blocked_by"])
	}
	if _, hasLabels := items[0].(map[string]any)["labels"]; hasLabels && items[0].(map[string]any)["labels"] != nil {
		t.Errorf("--repo none adds no label: %v", items[0])
	}
	if !strings.Contains(out, `"items"`) {
		t.Errorf("--json prints the whole answer: %s", out)
	}
}

func TestIssueListSendsEveryFilterAndRepeatsStatusAndLabel(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `{"items":[],"total":0,"has_more":false,"next_cursor":null}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, errOut := run(t, server, repository(t, "project = PLAN\n"), "issue", "list",
		"--status", "todo", "--status", "in_progress", "--label", "bug", "--label", "cut-1", "--ready", "--priority-min", "2",
		"--epic", "none", "--assignee", "me", "--claimed", "false", "--author", "maintainer", "--blocked", "--deleted", "--query", "claim expired", "--sort", "priority", "--limit", "10")
	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}

	q := f.requests[0].URL.Query()
	if got := q["status"]; len(got) != 2 || got[0] != "todo" || got[1] != "in_progress" {
		t.Errorf("status = %v", got)
	}
	if got := q["label"]; len(got) != 2 {
		t.Errorf("label = %v", got)
	}
	for key, want := range map[string]string{"project": "PLAN", "ready": "true", "priority_min": "2", "epic": "none", "assignee": "me", "claimed": "false", "author": "maintainer", "blocked": "true", "deleted": "true", "q": "claim expired", "sort": "priority", "limit": "10"} {
		if q.Get(key) != want {
			t.Errorf("%s = %q, want %q", key, q.Get(key), want)
		}
	}
	if q.Has("priority_max") || q.Has("has_open_question") {
		t.Errorf("flags not given are not sent: %v", q)
	}
}

func TestIssueEditSendsOnlyWhatWasGivenAndTheIfMatch(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, issue }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, errOut := run(t, server, t.TempDir(), "issue", "edit", "PLAN-42", "--title", "After", "--assignee", "none", "--epic", "PLAN-E2", "--label", "feature", "--ready", "true", "--if-match", "2026-09-02T14:03:07.123456Z")
	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}

	req := f.requests[0]
	if req.Method != http.MethodPatch || req.URL.Path != "/issues/PLAN-42" {
		t.Fatalf("request %s %s", req.Method, req.URL)
	}
	if got := req.Header.Get("If-Match"); got != `"2026-09-02T14:03:07.123456Z"` {
		t.Errorf("If-Match = %q", got)
	}
	var body map[string]any
	_ = json.Unmarshal([]byte(f.bodies[0]), &body)
	if len(body) != 5 || body["title"] != "After" || body["assignee"] != nil || body["epic"] != "PLAN-E2" || body["ready"] != true {
		t.Errorf("body = %v; only the given fields, assignee as null", body)
	}
	if _, present := body["assignee"]; !present {
		t.Error("`none` is sent as null, not left out")
	}
	if _, present := body["description"]; present {
		t.Error("a field not given is not sent")
	}

	code, _, errOut = run(t, server, t.TempDir(), "issue", "edit", "PLAN-42")
	if code != exit.Usage || !strings.Contains(errOut, "nothing to change") {
		t.Errorf("no fields: code %d, stderr %q", code, errOut)
	}
}

func TestIssueEditAndDeleteUseTheBulkEndpointsForSeveralKeys(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method == http.MethodPatch {
			return 200, `{"items":[` + issue + `,` + issue + `]}`
		}
		return 204, ""
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := t.TempDir()

	code, out, errOut := run(t, server, dir, "issue", "edit", "PLAN-2", "PLAN-1", "--priority", "3")
	if code != exit.OK || errOut != "" || strings.Count(out, "PLAN-42") != 2 {
		t.Fatalf("bulk edit: code %d, stdout %q, stderr %q", code, out, errOut)
	}
	if request := f.requests[0]; request.Method != http.MethodPatch || request.URL.Path != "/issues" {
		t.Fatalf("bulk edit request: %s %s", request.Method, request.URL.Path)
	}
	var changed map[string]any
	_ = json.Unmarshal([]byte(f.bodies[0]), &changed)
	keys := changed["keys"].([]any)
	changes := changed["changes"].(map[string]any)
	if keys[0] != "PLAN-2" || keys[1] != "PLAN-1" || changes["priority"] != float64(3) {
		t.Errorf("bulk edit body = %v", changed)
	}

	code, out, errOut = run(t, server, dir, "issue", "delete", "plan-2", "plan-1")
	if code != exit.OK || errOut != "" || !strings.Contains(out, "PLAN-2 deleted") || !strings.Contains(out, "PLAN-1 deleted") {
		t.Fatalf("bulk delete: code %d, stdout %q, stderr %q", code, out, errOut)
	}
	if request := f.requests[1]; request.Method != http.MethodDelete || request.URL.Path != "/issues" {
		t.Fatalf("bulk delete request: %s %s", request.Method, request.URL.Path)
	}
	var deleted map[string]any
	_ = json.Unmarshal([]byte(f.bodies[1]), &deleted)
	if got := deleted["keys"].([]any); got[0] != "plan-2" || got[1] != "plan-1" {
		t.Errorf("bulk delete body = %v", deleted)
	}
	if f.requests[0].Header.Get("Idempotency-Key") == "" || f.requests[1].Header.Get("Idempotency-Key") == "" {
		t.Error("each bulk request is one idempotent write")
	}

	code, _, errOut = run(t, server, dir, "issue", "edit", "PLAN-1", "PLAN-2", "--title", "x", "--if-match", "2026-09-02T14:03:07Z")
	if code != exit.Usage || !strings.Contains(errOut, "--if-match") {
		t.Errorf("bulk --if-match: code %d, stderr %q", code, errOut)
	}
}

func TestIssueViewDeleteRestoreHistoryAndTheEdges(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.Method == http.MethodDelete && r.URL.Path == "/issues/PLAN-42":
			return 204, ""
		case r.URL.Path == "/issues/PLAN-42/history":
			return 200, `[{"id":1,"actor":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},"at":"2026-09-02T14:00:00.000000Z","field":"created","old_value":null,"new_value":null,"note":null},
			{"id":2,"actor":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},"at":"2026-09-02T14:03:07.000000Z","field":"claim","old_value":null,"new_value":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},"note":"expired"}]`
		case r.URL.Path == "/issues/PLAN-99":
			return 404, `{"type":"/problems/deleted","title":"deleted","status":404,"detail":"Issue PLAN-99 is deleted.","restorable_until":"2026-09-09T00:00:00Z"}`
		default:
			return 200, issue
		}
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := t.TempDir()

	code, out, _ := run(t, server, dir, "issue", "view", "PLAN-42")
	if code != exit.OK || !strings.Contains(out, "PLAN-42  Settle the claim") {
		t.Fatalf("view: code %d, out %s", code, out)
	}
	code, out, _ = run(t, server, dir, "issue", "view", "PLAN-42", "--json")
	if code != exit.OK || !strings.Contains(out, `"key": "PLAN-42"`) || !strings.Contains(out, `"project_context"`) {
		t.Fatalf("view --json prints the complete object: %s", out)
	}

	code, _, errOut := run(t, server, dir, "issue", "view", "PLAN-99")
	if code != exit.NotFound || !strings.Contains(errOut, "(deleted)") {
		t.Fatalf("deleted: code %d, stderr %q", code, errOut)
	}

	code, out, _ = run(t, server, dir, "issue", "delete", "plan-42")
	if code != exit.OK || !strings.Contains(out, "PLAN-42 deleted") {
		t.Fatalf("delete: code %d, out %s", code, out)
	}
	if f.requests[len(f.requests)-1].Header.Get("Idempotency-Key") == "" {
		t.Error("a delete is a write and carries a key")
	}

	code, out, _ = run(t, server, dir, "issue", "restore", "PLAN-42")
	if code != exit.OK || !strings.Contains(out, "PLAN-42") {
		t.Fatalf("restore: code %d", code)
	}

	code, out, _ = run(t, server, dir, "issue", "history", "PLAN-42")
	if code != exit.OK || !strings.Contains(out, "created") || !strings.Contains(out, "claim") || !strings.Contains(out, "→ quiet-otter-42") || !strings.Contains(out, "(expired)") {
		t.Fatalf("history: code %d, out %s", code, out)
	}

	code, _, _ = run(t, server, dir, "issue", "label", "add", "PLAN-42", "feature")
	last := f.requests[len(f.requests)-1]
	if code != exit.OK || last.Method != http.MethodPost || last.URL.Path != "/issues/PLAN-42/labels/feature" {
		t.Fatalf("label add: code %d, %s %s", code, last.Method, last.URL.Path)
	}
	code, _, _ = run(t, server, dir, "issue", "block", "PLAN-42", "--by", "PLAN-40")
	last = f.requests[len(f.requests)-1]
	if code != exit.OK || last.Method != http.MethodPost || last.URL.Path != "/issues/PLAN-42/blocked-by/PLAN-40" {
		t.Fatalf("block: code %d, %s %s", code, last.Method, last.URL.Path)
	}
	code, _, errOut = run(t, server, dir, "issue", "unblock", "PLAN-42")
	if code != exit.Usage || !strings.Contains(errOut, "--by") {
		t.Fatalf("unblock without --by: code %d, stderr %q", code, errOut)
	}
}
