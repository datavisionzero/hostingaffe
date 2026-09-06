package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

func TestExportReadsEveryPageAndCompleteObject(t *testing.T) {
	var f *fake
	f = &fake{t: t, version: "1.2.0", answer: func(r *http.Request) (int, string) {
		switch r.URL.Path {
		case "/version":
			return 200, `{"version":"1.2.0"}`
		case "/projects/PLAN":
			return 200, project
		case "/projects/PLAN/labels":
			return 200, `[` + label + `]`
		case "/epics":
			if r.URL.Query().Get("cursor") == "epic-next" {
				return 200, `{"items":[],"total":1,"has_more":false,"next_cursor":null}`
			}
			return 200, `{"items":[{"key":"PLAN-E2","project":"PLAN","title":"Backend","status":"open","labels":["bug"],"progress":{"total":7,"closed":5,"done":4,"canceled":1},"created_at":"2026-09-02T14:00:00Z","updated_at":"2026-09-02T14:00:00Z","closed_at":null}],"total":1,"has_more":true,"next_cursor":"epic-next"}`
		case "/epics/PLAN-E2":
			return 200, epic
		case "/projects/PLAN/releases":
			return 200, `[{"name":"v1.0.0","status":"published","description":"First release.","published_at":"2026-09-04T12:00:00Z","published_by":null,"issues":1}]`
		case "/projects/PLAN/releases/v1.0.0":
			return 200, release
		case "/issues":
			if r.URL.Query().Get("cursor") == "issue-next" {
				return 200, `{"items":[],"total":1,"has_more":false,"next_cursor":null}`
			}
			return 200, `{"items":[{"key":"PLAN-42","project":"PLAN","title":"Settle the claim","status":"in_progress","ready":true,"priority":3,"labels":[],"epic":null,"parent":null,"release":null,"assignee":null,"claim":null,"blocked_by":[],"open_questions":0,"open_blockers":0,"open_sub_issues":0,"created_at":"2026-09-02T14:00:00Z","updated_at":"2026-09-02T14:03:07Z","closed_at":null,"deleted_at":null,"deleted_by":null}],"total":1,"has_more":true,"next_cursor":"issue-next"}`
		case "/issues/PLAN-42":
			return 200, issue
		case "/issues/PLAN-42/history":
			return 200, `[{"id":7,"at":"2026-09-02T14:03:07Z","actor":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},"field":"status","old_value":"todo","new_value":"in_progress","note":null}]`
		default:
			return 404, `{"type":"/problems/not-found","status":404}`
		}
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, errOut := run(t, server, repository(t, "project = PLAN\n"), "export", "--json")
	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
	var document struct {
		ExportedAt string           `json:"exported_at"`
		Hostingaffe   string           `json:"hostingaffe"`
		Project    map[string]any   `json:"project"`
		Labels     []map[string]any `json:"labels"`
		Epics      []map[string]any `json:"epics"`
		Releases   []map[string]any `json:"releases"`
		Issues     []map[string]any `json:"issues"`
	}
	if err := json.Unmarshal([]byte(out), &document); err != nil {
		t.Fatal(err)
	}
	if _, err := time.Parse("2006-01-02T15:04:05.000000Z", document.ExportedAt); err != nil {
		t.Errorf("exported_at %q is not UTC with microseconds", document.ExportedAt)
	}
	if document.Hostingaffe != "1.2.0" || document.Project["key"] != "PLAN" || len(document.Labels) != 1 || len(document.Epics) != 1 || len(document.Releases) != 1 || len(document.Issues) != 1 {
		t.Fatalf("incomplete export: %#v", document)
	}
	if document.Epics[0]["description"] != "The plan." || document.Releases[0]["issues"] == nil {
		t.Error("epics and releases must be complete objects")
	}
	if document.Issues[0]["description"] != "Four columns." || len(document.Issues[0]["history"].([]any)) != 1 {
		t.Error("issues must contain their complete object and history")
	}

	var sawEpicCursor, sawIssueCursor bool
	for _, request := range f.requests {
		query := request.URL.Query()
		if request.URL.Path == "/epics" {
			if query.Get("status") != "all" || query.Get("limit") != "200" {
				t.Errorf("epic list query: %s", request.URL.RawQuery)
			}
			sawEpicCursor = sawEpicCursor || query.Get("cursor") == "epic-next"
		}
		if request.URL.Path == "/issues" {
			if query.Get("limit") != "200" || len(query["status"]) != 6 {
				t.Errorf("issue list query: %s", request.URL.RawQuery)
			}
			sawIssueCursor = sawIssueCursor || query.Get("cursor") == "issue-next"
		}
	}
	if !sawEpicCursor || !sawIssueCursor {
		t.Error("export did not follow every collection cursor")
	}
}

func TestExportRequiresJSONAndWritesNothingWhenAReadFails(t *testing.T) {
	server := httptest.NewServer((&fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Path == "/version" {
			return 200, `{"version":"0.0.0-dev"}`
		}
		return 404, `{"type":"/problems/not-found","status":404,"detail":"PLAN was not found."}`
	}}).handler())
	defer server.Close()
	dir := repository(t, "project = PLAN\n")

	code, out, errOut := run(t, server, dir, "export")
	if code != exit.Usage || out != "" || !strings.Contains(errOut, "pass --json") {
		t.Errorf("without json: code %d, stdout %q, stderr %q", code, out, errOut)
	}
	code, out, errOut = run(t, server, dir, "export", "--json")
	if code != exit.NotFound || out != "" || !strings.Contains(errOut, "PLAN was not found") {
		t.Errorf("failed read: code %d, stdout %q, stderr %q", code, out, errOut)
	}
}
