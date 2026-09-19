package cmd

import (
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

// An act over two fields, and a deployment: one line each, whatever the number
// of rows behind them.
const eventsJSON = `[
{"at":"2026-09-18T19:12:04.118231Z","actor":{"id":"0199a000-0000-7000-8000-000000000001","kind":"user","name":"maintainer"},
 "subject_kind":"deployment","subject":"logaffe-prod","number":7,"machine":"ex44","owner":null,
 "changes":[{"field":"version","old_value":"0.4.1","new_value":"0.5.0"}],"note":"LOG-88","cursor":"one"},
{"at":"2026-09-18T08:00:00Z","actor":{"id":"0199a000-0000-7000-8000-000000000001","kind":"user","name":"maintainer"},
 "subject_kind":"machine","subject":"ex44","number":null,"machine":"ex44","owner":null,
 "changes":[{"field":"status","old_value":"planned","new_value":"active"},
            {"field":"created","old_value":null,"new_value":null}],"note":null,"cursor":"two"}]`

func TestHistoryIsOneLinePerAct(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, eventsJSON }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "history", "--machine", "ex44")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	if len(f.requests) != 1 {
		t.Fatalf("%d requests: without a window it is one page", len(f.requests))
	}
	if got := f.requests[0].URL.Path; got != "/api/history" {
		t.Errorf("path %q", got)
	}
	if got := f.requests[0].URL.Query().Get("machine"); got != "ex44" {
		t.Errorf("machine = %q", got)
	}

	lines := strings.Split(strings.TrimSuffix(out, "\n"), "\n")
	if len(lines) != 2 {
		t.Fatalf("one line per event:\n%s", out)
	}
	if !strings.Contains(lines[0], "logaffe-prod #7") || !strings.Contains(lines[0], "0.4.1 → 0.5.0") {
		t.Errorf("the deployment: %q", lines[0])
	}
	if !strings.Contains(lines[0], "(LOG-88)") {
		t.Errorf("the note: %q", lines[0])
	}
	// Two fields of one act on one line, and a field with no values is the
	// field and nothing else.
	if !strings.Contains(lines[1], "status planned → active, created") {
		t.Errorf("the act: %q", lines[1])
	}
}

// A window is what `ha` adds on top of the endpoint's page: it walks until the
// events leave it, and prints nothing older.
func TestHistorySinceWalksUntilTheWindowIsEmpty(t *testing.T) {
	now := time.Now().UTC()
	fresh := func(cursor string, ago time.Duration) string {
		return fmt.Sprintf(`{"at":%q,"actor":{"id":"0199a000-0000-7000-8000-000000000001","kind":"user","name":"maintainer"},
		 "subject_kind":"machine","subject":"ex44","number":null,"machine":"ex44","owner":null,
		 "changes":[{"field":"os","old_value":null,"new_value":"Debian 12"}],"note":null,"cursor":%q}`,
			now.Add(-ago).Format(time.RFC3339Nano), cursor)
	}

	pages := []string{
		"[" + fresh("a", time.Hour) + "," + fresh("b", 2*time.Hour) + "]",
		"[" + fresh("c", 3*time.Hour) + "," + fresh("d", 400*time.Hour) + "]",
	}
	asked := 0
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		page := pages[min(asked, len(pages)-1)]
		asked++
		return 200, page
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "history", "--since", "7d")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	if len(f.requests) != 2 {
		t.Fatalf("%d requests: the walk stops at the first event outside the window", len(f.requests))
	}
	if got := f.requests[1].URL.Query().Get("before"); got != "b" {
		t.Errorf("before = %q: the walk goes on from the last event of the page", got)
	}

	lines := strings.Split(strings.TrimSuffix(out, "\n"), "\n")
	if len(lines) != 3 {
		t.Fatalf("three events are inside the window, the fourth is not:\n%s", out)
	}
}

func TestHistoryRefusesASinceItCannotRead(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, eventsJSON }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := run(t, server, "history", "--since", "last Tuesday")
	if code != exit.Usage {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if !strings.Contains(stderr, "24h") {
		t.Errorf("the message says what a span looks like: %q", stderr)
	}
	if len(f.requests) != 0 {
		t.Error("nothing is asked of the instance for a window ha cannot read")
	}
}

func TestWindowReadsSpansAndMoments(t *testing.T) {
	now := time.Date(2026, 9, 19, 12, 0, 0, 0, time.UTC)

	for _, c := range []struct {
		since string
		want  time.Time
	}{
		{"24h", now.Add(-24 * time.Hour)},
		{"90m", now.Add(-90 * time.Minute)},
		{"7d", now.AddDate(0, 0, -7)},
		{"2w", now.AddDate(0, 0, -14)},
		{"2026-09-01T00:00:00Z", time.Date(2026, 9, 1, 0, 0, 0, 0, time.UTC)},
	} {
		at, err := window(c.since, now)
		if err != nil {
			t.Fatalf("--since %s: %v", c.since, err)
		}
		if !at.Equal(c.want) {
			t.Errorf("--since %s = %s, want %s", c.since, at, c.want)
		}
	}

	if at, err := window("", now); at != nil || err != nil {
		t.Errorf("no window is no floor: %v, %v", at, err)
	}
}
