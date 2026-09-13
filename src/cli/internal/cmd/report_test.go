package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// `collect` needs no token and no instance: it is the transparency path, and it
// runs on a machine with no network.
func TestReportCollectPrintsWhatWouldLeaveTheHost(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		t.Fatal("collect asked the instance something")
		return 500, ""
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := run(t, server, "report", "collect")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}

	var report map[string]any
	if err := json.Unmarshal([]byte(stdout), &report); err != nil {
		t.Fatalf("not the JSON a host would send: %v\n%s", err, stdout)
	}
	if _, said := report["collected_at"]; !said {
		t.Fatalf("no collected_at: %s", stdout)
	}
	if _, said := report["missing"]; !said {
		t.Fatalf("no missing: %s", stdout)
	}
	if len(f.requests) != 0 {
		t.Fatalf("collect made %d requests", len(f.requests))
	}
}

func TestReportSendHandsInAndSaysWhatCameBack(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method != http.MethodPost || r.URL.Path != "/api/machines/ex44/reports" {
			t.Fatalf("asked %s %s", r.Method, r.URL.Path)
		}
		return 201, `{"number":7,"received_at":"2026-09-13T08:00:09Z"}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := run(t, server, "report", "send", "ex44")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	if !strings.Contains(stdout, "report 7") || !strings.Contains(stdout, "ex44") {
		t.Fatalf("output: %q", stdout)
	}

	// The body carries the sections the contract names and nothing a container
	// holds beside them.
	var body map[string]any
	if err := json.Unmarshal([]byte(f.bodies[0]), &body); err != nil {
		t.Fatalf("body is not JSON: %v", err)
	}
	if _, said := body["collected_at"]; !said {
		t.Fatalf("body: %s", f.bodies[0])
	}
	for _, forbidden := range []string{"\"Env\"", "\"Command\"", "\"Labels\""} {
		if strings.Contains(f.bodies[0], forbidden) {
			t.Fatalf("%s reached the instance: %s", forbidden, f.bodies[0])
		}
	}
}

// `--quiet` is what the cron line carries: nothing on success, so that no mail
// is written every quarter of an hour.
func TestReportSendQuietSaysNothingOnSuccess(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 201, `{"number":1,"received_at":"2026-09-13T08:00:09Z"}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, _ := run(t, server, "report", "send", "ex44", "--quiet")
	if code != 0 {
		t.Fatalf("exit %d", code)
	}
	if strings.TrimSpace(stdout) != "" {
		t.Fatalf("quiet said %q", stdout)
	}
}

// A token that does not admit the machine is exit 7, so that a person running
// the cron line by hand reads the code and knows which half is wrong.
func TestReportSendWithARefusedTokenIsDenied(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 401, `{"type":"/problems/unauthenticated","title":"No token","status":401,"detail":"The presented token is not a machine's."}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	if code, _, _ := run(t, server, "report", "send", "ex44"); code != 7 {
		t.Fatalf("exit %d", code)
	}
}

// Which machine this host is has to come from somewhere, and the message says
// from where. It is exit 2, before a request goes out.
func TestReportSendWithoutAMachineSaysWhereToPutIt(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 201, "{}" }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := run(t, server, "report", "send")
	if code != 2 || !strings.Contains(stderr, "HOSTINGAFFE_MACHINE") {
		t.Fatalf("exit %d, stderr %q", code, stderr)
	}
	if len(f.requests) != 0 {
		t.Fatal("a request went out before the machine was known")
	}
}

// A token in a file anybody on the machine can read is refused: a file mode is
// the only protection a token in a file has.
func TestReportSendRefusesATokenFileOthersCanRead(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 201, `{"number":1,"received_at":"2026-09-13T08:00:09Z"}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	path := filepath.Join(t.TempDir(), "token")
	if err := os.WriteFile(path, []byte("ha_a-machine-token-of-thirty-two-characters"), 0o644); err != nil {
		t.Fatal(err)
	}

	code, _, stderr := runWithout(t, server, "report", "send", "ex44", "--token-file", path)
	if code != 2 || !strings.Contains(stderr, "readable by others") {
		t.Fatalf("exit %d, stderr %q", code, stderr)
	}

	if err := os.Chmod(path, 0o600); err != nil {
		t.Fatal(err)
	}
	if code, _, stderr := runWithout(t, server, "report", "send", "ex44", "--token-file", path); code != 0 {
		t.Fatalf("exit %d, stderr %q", code, stderr)
	}
}
