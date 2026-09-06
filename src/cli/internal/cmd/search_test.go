package cmd

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const hitsJSON = `[
{"kind":"installation","key":"logaffe-prod","name":"logaffe","number":null,"owner":null,"where":"ports"},
{"kind":"deployment","key":"logaffe-prod","name":"1.4.0","number":3,
 "owner":{"kind":"installation","key":"logaffe-prod"},"where":"fields"},
{"kind":"file","key":"compose.override.yml","name":"","number":null,
 "owner":{"kind":"installation","key":"logaffe-prod"},"where":"content"},
{"kind":"page","key":"backup-restore","name":"Restoring a backup","number":null,
 "owner":{"kind":"machine","key":"ex44"},"where":"body"}]`

// One call, and what comes back says what was found and where.
func TestSearchIsOneCallAndSaysWhereEachHitIs(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, hitsJSON }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "search", "18502")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	if len(f.requests) != 1 {
		t.Fatalf("%d requests", len(f.requests))
	}
	last := f.requests[0]
	if last.Method != http.MethodGet || last.URL.Path != "/api/search" {
		t.Errorf("%s %s", last.Method, last.URL.Path)
	}
	if got := last.URL.Query().Get("q"); got != "18502" {
		t.Errorf("q = %q", got)
	}
	if last.URL.Query().Has("limit") {
		t.Error("no --limit, no parameter: the instance's default is the default")
	}

	lines := strings.Split(strings.TrimSuffix(out, "\n"), "\n")
	if len(lines) != 4 {
		t.Fatalf("one line per hit:\n%s", out)
	}
	if !strings.HasPrefix(lines[0], "installation  logaffe-prod") || !strings.Contains(lines[0], "ports") {
		t.Errorf("the port hit: %q", lines[0])
	}
	// A deployment is addressed by its installation and its number.
	if !strings.Contains(lines[1], "logaffe-prod #3") {
		t.Errorf("the deployment hit: %q", lines[1])
	}
	// A file and a page say what they belong to.
	if !strings.Contains(lines[2], "installation logaffe-prod") {
		t.Errorf("the file hit: %q", lines[2])
	}
	if !strings.Contains(lines[3], "machine ex44") || !strings.Contains(lines[3], "Restoring a backup") {
		t.Errorf("the page hit: %q", lines[3])
	}
}

func TestSearchPassesTheLimitOnlyWhenAsked(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, hitsJSON }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	if code, _, _ := run(t, server, "search", "logaffe", "--limit", "5"); code != exit.OK {
		t.Fatal("code")
	}
	if got := f.requests[len(f.requests)-1].URL.Query().Get("limit"); got != "5" {
		t.Errorf("limit = %q", got)
	}
}

// A search for nothing never leaves: the instance would refuse it, and so does
// the argument count.
func TestSearchForNothingIsExitTwo(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, `[]` }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	if code, _, stderr := run(t, server, "search"); code != exit.Usage || stderr == "" {
		t.Errorf("code %d, stderr %q", code, stderr)
	}
}
