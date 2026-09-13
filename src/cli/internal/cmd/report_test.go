package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
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

const aStoredReport = `{"machine":"ex44","number":7,"received_at":"2026-09-13T08:00:09Z","collected_at":"2026-09-13T08:00:07Z","agent":"0.4.0",
"host":{"hostname":"ex44","os":"Ubuntu 26.04 LTS","kernel":"6.14.0-27-generic","arch":"x86_64","uptime_seconds":1893244,"load1":0.14,"load5":0.2,"load15":0.18},
"memory":{"total_bytes":67430400000,"used_bytes":19204000000,"available_bytes":46900000000,"swap_total_bytes":0,"swap_used_bytes":0},
"disks":[{"mount":"/","device":"/dev/nvme0n1p2","size_bytes":502000000000,"used_bytes":301000000000,"percent":60}],
"containers":[{"name":"logaffe","image":"ghcr.io/datavisionzero/logaffe:1.4.0","state":"running","status":"Up 3 days","health":"healthy","restarts":0,"started_at":"2026-09-10T09:12:00Z","ports":["127.0.0.1:18502->8080/tcp"]},
{"name":"caddy","image":"caddy:2.10","state":"exited","status":"Exited (0)","health":null,"restarts":3,"started_at":null,"ports":[]}],
"missing":[{"section":"disks","reason":"df is not on the PATH"}]}`

func TestReportShowSetsTheSectionsOutForAPerson(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Path != "/api/machines/ex44/reports/latest" {
			t.Fatalf("asked %s", r.URL.Path)
		}
		return 200, aStoredReport
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := run(t, server, "report", "show", "ex44")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}

	for _, want := range []string{
		"ex44", "report 7", "Ubuntu 26.04 LTS", "load: 0.14 0.20 0.18",
		"GB", "60%", "/dev/nvme0n1p2", "1 of 2 running",
		"ghcr.io/datavisionzero/logaffe:1.4.0", "running (healthy)", "restarts: 3",
	} {
		if !strings.Contains(stdout, want) {
			t.Fatalf("%q is not in the output:\n%s", want, stdout)
		}
	}

	// A section the collector could not determine is said, not swallowed.
	if !strings.Contains(stdout, "disks: not determined (df is not on the PATH)") {
		t.Fatalf("the missing section was passed over:\n%s", stdout)
	}

	// Sizes are what a person reads; the numbers are one --json away.
	if strings.Contains(stdout, "502000000000") {
		t.Fatalf("bytes reached a person's screen:\n%s", stdout)
	}
}

func TestReportShowByNumberAsksForThatOne(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Path != "/api/machines/ex44/reports/3" {
			t.Fatalf("asked %s", r.URL.Path)
		}
		return 200, aStoredReport
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	if code, _, stderr := run(t, server, "report", "show", "ex44", "--number", "3"); code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
}

func TestReportListIsOneLinePerReport(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Path != "/api/machines/ex44/reports" || r.URL.Query().Get("limit") != "2" {
			t.Fatalf("asked %s?%s", r.URL.Path, r.URL.RawQuery)
		}
		return 200, `{"total":2,"reports":[
			{"number":7,"received_at":"2026-09-13T08:00:09Z","collected_at":"2026-09-13T08:00:07Z","containers_running":4,"containers_total":5,"disk_percent":91,"load1":0.14},
			{"number":6,"received_at":"2026-09-13T07:45:09Z","collected_at":"2026-09-13T07:45:07Z","containers_running":5,"containers_total":5,"disk_percent":60,"load1":0.2}]}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := run(t, server, "report", "list", "ex44", "--limit", "2")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	for _, want := range []string{"4/5", "91%", "5/5", "60%"} {
		if !strings.Contains(stdout, want) {
			t.Fatalf("%q is not in the output:\n%s", want, stdout)
		}
	}
	if lines := strings.Count(strings.TrimSpace(stdout), "\n") + 1; lines != 2 {
		t.Fatalf("%d lines for two reports:\n%s", lines, stdout)
	}
}

// A machine that has never reported is not an error, and the sentence about it
// goes to stderr so that a pipeline reads nothing on stdout.
func TestReportListOfAMachineThatNeverReported(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `{"total":0,"reports":[]}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := run(t, server, "report", "list", "ex44")
	if code != 0 {
		t.Fatalf("exit %d", code)
	}
	if strings.TrimSpace(stdout) != "" {
		t.Fatalf("stdout said %q", stdout)
	}
	if !strings.Contains(stderr, "never reported") {
		t.Fatalf("stderr said %q", stderr)
	}
}

// `last seen` is a time and never a judgement: no threshold, no word like
// "stale" (VISION 5).
func TestMachineListSaysWhenEachLastSpoke(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `[{"key":"ex44","name":"ex44","kind":"dedicated","status":"active","provider":"hetzner","location":"fsn1-dc14","arch":"amd64","measured_at":null,"last_seen":"` +
			time.Now().Add(-12*time.Minute).UTC().Format(time.RFC3339) + `","updated_at":"2026-09-13T08:00:00Z"},
			{"key":"cx22","name":"cx22","kind":"vps","status":"active","provider":null,"location":null,"arch":null,"measured_at":null,"last_seen":null,"updated_at":"2026-09-13T08:00:00Z"}]`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := run(t, server, "machine", "list")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	if !strings.Contains(stdout, "12 minutes ago") {
		t.Fatalf("nothing said about when ex44 last spoke:\n%s", stdout)
	}
	for _, judgement := range []string{"stale", "silent", "down", "OK"} {
		if strings.Contains(stdout, judgement) {
			t.Fatalf("%q is a judgement, and the CLI makes none:\n%s", judgement, stdout)
		}
	}
}
