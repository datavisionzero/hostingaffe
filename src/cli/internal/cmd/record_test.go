package cmd

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
)

const (
	identities = `"created_by":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"updated_by":{"id":"0198e0c0-0000-7000-8000-000000000001","kind":"agent","name":"quiet-otter-42"},
"created_at":"2026-09-05T10:00:00.000000Z","updated_at":"2026-09-05T12:00:00.000000Z"`

	machineJSON = `{"key":"ex44","name":"The big one","hostname":"ex44","kind":"dedicated","host":null,
"provider":"hetzner","plan":"EX44","location":"fsn1-dc14","os":"Ubuntu 26.04 LTS","arch":"amd64",
"cpu":"Intel i5-13500","memory":"64G","disk":"2×512G NVMe ZFS mirror","ipv4":"192.0.2.10","ipv6":"2001:db8::1",
"private_ip":"198.51.100.7","ssh":"ex44","status":"active","measured_at":"2026-09-01T08:00:00.000000Z",
"description":"The box everything else sits on.",` + identities + `}`

	softwareJSON = `{"key":"logaffe","name":"logaffe","homepage":"https://example.test",
"repository":"https://github.com/datavisionzero/logaffe","image":"ghcr.io/datavisionzero/logaffe",
"description":"The log.",` + identities + `}`

	installationJSON = `{"key":"logaffe-prod","name":"Logaffe production","machine":"ex44","software":"logaffe",
"environment":"production","role":"application","status":"active","urls":["https://logs.example.test"],
"ports":[{"port":443,"protocol":"tcp","scope":"public"},{"port":5432,"protocol":"tcp","scope":"private"}],
"path":"/srv/logaffe","secrets":["LOGAFFE_DB_PASSWORD"],"backup":"active","monitoring":"external",
"logging":"central","version":"1.4.0","description":"The one people look at.",` + identities + `}`

	historyJSON = `[{"id":1,"actor":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"at":"2026-09-05T10:00:00Z","field":"created","old_value":null,"new_value":null,"note":"replacing the old one"},
{"id":2,"actor":{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer"},
"at":"2026-09-05T12:00:00Z","field":"os","old_value":"Ubuntu 24.04","new_value":"Ubuntu 26.04 LTS","note":"dist-upgrade"}]`
)

// records answers whatever a record verb asks for, so that one server serves
// every verb of all three objects.
func records() *fake {
	return &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		object := machineJSON
		switch {
		case strings.HasPrefix(r.URL.Path, "/api/software"):
			object = softwareJSON
		case strings.HasPrefix(r.URL.Path, "/api/installations"):
			object = installationJSON
		}

		switch {
		case r.Method == http.MethodDelete:
			return 204, ""
		case strings.HasSuffix(r.URL.Path, "/history"):
			return 200, historyJSON
		case r.Method == http.MethodPost && !strings.HasSuffix(r.URL.Path, "/restore"):
			return 201, object
		case r.Method == http.MethodGet && !strings.Contains(strings.TrimPrefix(r.URL.Path, "/api/"), "/"):
			return 200, "[" + object + "]"
		default:
			return 200, object
		}
	}}
}

// The verbs of VISION 6.1 — add, set, view, list, delete, restore, history —
// for the three objects the product is about, each at the address the contract
// gives it.
func TestRecordVerbsReachTheRightAddresses(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, tc := range []struct {
		args                   []string
		method, path, contains string
	}{
		{[]string{"machine", "list"}, "GET", "/api/machines", "ex44"},
		{[]string{"machine", "view", "ex44"}, "GET", "/api/machines/ex44", "The big one"},
		{[]string{"machine", "add", "ex44", "--kind", "dedicated"}, "POST", "/api/machines", "ex44"},
		{[]string{"machine", "set", "ex44", "--os", "Ubuntu 26.04 LTS"}, "PATCH", "/api/machines/ex44", "ex44"},
		{[]string{"machine", "delete", "ex44"}, "DELETE", "/api/machines/ex44", "ha machine restore ex44"},
		{[]string{"machine", "restore", "ex44"}, "POST", "/api/machines/ex44/restore", "ex44"},
		{[]string{"machine", "history", "ex44"}, "GET", "/api/machines/ex44/history", "dist-upgrade"},

		{[]string{"software", "list"}, "GET", "/api/software", "logaffe"},
		{[]string{"software", "view", "logaffe"}, "GET", "/api/software/logaffe", "logaffe"},
		{[]string{"software", "add", "logaffe"}, "POST", "/api/software", "logaffe"},
		{[]string{"software", "set", "logaffe", "--image", "ghcr.io/datavisionzero/logaffe"}, "PATCH", "/api/software/logaffe", "logaffe"},
		{[]string{"software", "delete", "logaffe"}, "DELETE", "/api/software/logaffe", "ha software restore logaffe"},
		{[]string{"software", "restore", "logaffe"}, "POST", "/api/software/logaffe/restore", "logaffe"},
		{[]string{"software", "history", "logaffe"}, "GET", "/api/software/logaffe/history", "dist-upgrade"},

		{[]string{"installation", "list"}, "GET", "/api/installations", "logaffe-prod"},
		{[]string{"inst", "view", "logaffe-prod"}, "GET", "/api/installations/logaffe-prod", "Logaffe production"},
		{[]string{"inst", "add", "logaffe-prod", "--machine", "ex44", "--software", "logaffe",
			"--environment", "production", "--role", "application"}, "POST", "/api/installations", "logaffe-prod"},
		{[]string{"inst", "set", "logaffe-prod", "--backup", "active"}, "PATCH", "/api/installations/logaffe-prod", "logaffe-prod"},
		{[]string{"inst", "delete", "logaffe-prod"}, "DELETE", "/api/installations/logaffe-prod", "ha installation restore logaffe-prod"},
		{[]string{"inst", "restore", "logaffe-prod"}, "POST", "/api/installations/logaffe-prod/restore", "logaffe-prod"},
		{[]string{"inst", "history", "logaffe-prod"}, "GET", "/api/installations/logaffe-prod/history", "dist-upgrade"},
	} {
		code, out, stderr := run(t, server, tc.args...)
		if code != exit.OK || stderr != "" {
			t.Fatalf("%v: code %d, stderr %q", tc.args, code, stderr)
		}
		last := f.requests[len(f.requests)-1]
		if last.Method != tc.method || last.URL.Path != tc.path {
			t.Errorf("%v: %s %s", tc.args, last.Method, last.URL.Path)
		}
		if !strings.Contains(out, tc.contains) {
			t.Errorf("%v: stdout %q lacks %q", tc.args, out, tc.contains)
		}
	}
}

// A write says only what it was given: a field left off leaves the field alone,
// and the empty string is what clears one (docs/api.md, Conventions).
func TestAWriteSaysOnlyWhatItWasGiven(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	if code, _, stderr := run(t, server, "machine", "set", "ex44", "--os", "Ubuntu 26.04 LTS", "--location", ""); code != exit.OK {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	body := map[string]any{}
	_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body)
	if len(body) != 2 || body["os"] != "Ubuntu 26.04 LTS" || body["location"] != "" {
		t.Errorf("body = %v", body)
	}
}

// `--note` on every write, and the instance takes it as a query parameter
// (ADR 0004).
func TestEveryWriteCarriesItsNote(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, args := range [][]string{
		{"machine", "add", "ex44", "--kind", "dedicated", "--note", "replacing the old one"},
		{"machine", "set", "ex44", "--os", "Ubuntu 26.04 LTS", "--note", "replacing the old one"},
		{"machine", "delete", "ex44", "--note", "replacing the old one"},
		{"machine", "restore", "ex44", "--note", "replacing the old one"},
		{"software", "add", "logaffe", "--note", "replacing the old one"},
		{"software", "set", "logaffe", "--name", "logaffe", "--note", "replacing the old one"},
		{"software", "delete", "logaffe", "--note", "replacing the old one"},
		{"software", "restore", "logaffe", "--note", "replacing the old one"},
		{"inst", "add", "logaffe-prod", "--machine", "ex44", "--software", "logaffe",
			"--environment", "production", "--role", "application", "--note", "replacing the old one"},
		{"inst", "set", "logaffe-prod", "--backup", "active", "--note", "replacing the old one"},
		{"inst", "delete", "logaffe-prod", "--note", "replacing the old one"},
		{"inst", "restore", "logaffe-prod", "--note", "replacing the old one"},
	} {
		if code, _, stderr := run(t, server, args...); code != exit.OK {
			t.Fatalf("%v: code %d, stderr %q", args, code, stderr)
		}
		if got := f.requests[len(f.requests)-1].URL.Query().Get("note"); got != "replacing the old one" {
			t.Errorf("%v: note = %q", args, got)
		}
	}

	// No note, no parameter: a write without one says nothing rather than
	// writing an empty note.
	if code, _, _ := run(t, server, "machine", "set", "ex44", "--os", "Ubuntu 26.04 LTS"); code != exit.OK {
		t.Fatal("code")
	}
	if f.requests[len(f.requests)-1].URL.Query().Has("note") {
		t.Error("an empty --note is not sent")
	}
}

// A port is written and read as `443/tcp:public`, and the field is the object
// (CONTEXT.md, Installation).
func TestPortsAreWrittenAsPeopleWriteThem(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := run(t, server, "inst", "set", "logaffe-prod",
		"--port", "443/tcp:public", "--port", "5432/tcp:private",
		"--url", "https://logs.example.test", "--secret", "LOGAFFE_DB_PASSWORD")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	body := map[string]any{}
	_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body)

	ports, _ := json.Marshal(body["ports"])
	want := `[{"port":443,"protocol":"tcp","scope":"public"},{"port":5432,"protocol":"tcp","scope":"private"}]`
	if string(ports) != want {
		t.Errorf("ports = %s", ports)
	}

	urls, _ := json.Marshal(body["urls"])
	if string(urls) != `["https://logs.example.test"]` {
		t.Errorf("urls = %s", urls)
	}
	secrets, _ := json.Marshal(body["secrets"])
	if string(secrets) != `["LOGAFFE_DB_PASSWORD"]` {
		t.Errorf("secrets = %s", secrets)
	}

	// A list is replaced whole, and `none` on its own is what clears one.
	if code, _, _ = run(t, server, "inst", "set", "logaffe-prod", "--port", "none"); code != exit.OK {
		t.Fatal("code")
	}
	body = map[string]any{}
	_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body)
	cleared, _ := json.Marshal(body["ports"])
	if string(cleared) != `[]` {
		t.Errorf("cleared ports = %s", cleared)
	}
}

// A day is a moment: that is how a person writes down when they last looked at
// a machine, and the contract wants RFC 3339.
func TestAMeasurementIsADayOrATimestamp(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	if code, _, stderr := run(t, server, "machine", "set", "ex44", "--measured-at", "2026-09-05"); code != exit.OK {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	body := map[string]any{}
	_ = json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body)
	if body["measured_at"] != "2026-09-05T00:00:00Z" {
		t.Errorf("measured_at = %v", body["measured_at"])
	}

	if code, _, _ := run(t, server, "machine", "set", "ex44", "--measured-at", "2026-09-05T08:00:00Z"); code != exit.OK {
		t.Fatal("an RFC 3339 timestamp is a moment too")
	}
}

// The guard is sent only when it is given, and quoted as the header wants it.
func TestAGuardedWriteSendsWhatItLastRead(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, args := range [][]string{
		{"machine", "set", "ex44", "--os", "x", "--if-match", "2026-09-05T12:00:00.000000Z"},
		{"software", "set", "logaffe", "--name", "x", "--if-match", "2026-09-05T12:00:00.000000Z"},
		{"inst", "set", "logaffe-prod", "--path", "/srv", "--if-match", "2026-09-05T12:00:00.000000Z"},
	} {
		if code, _, stderr := run(t, server, args...); code != exit.OK {
			t.Fatalf("%v: code %d, stderr %q", args, code, stderr)
		}
		if got := f.requests[len(f.requests)-1].Header.Get("If-Match"); got != `"2026-09-05T12:00:00.000000Z"` {
			t.Errorf("%v: If-Match = %q", args, got)
		}
	}

	if code, _, _ := run(t, server, "machine", "set", "ex44", "--os", "x"); code != exit.OK {
		t.Fatal("code")
	}
	if f.requests[len(f.requests)-1].Header.Get("If-Match") != "" {
		t.Error("no --if-match, no header")
	}
}

// A list narrows by what the flags say, and an empty flag is not a filter.
func TestListsNarrowByWhatTheyAreAsked(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	if code, _, _ := run(t, server, "inst", "list", "--environment", "production", "--backup", "none"); code != exit.OK {
		t.Fatal("code")
	}
	query := f.requests[len(f.requests)-1].URL.Query()
	if query.Get("environment") != "production" || query.Get("backup") != "none" {
		t.Errorf("query = %v", query)
	}
	if query.Has("machine") || query.Has("retired") {
		t.Errorf("an empty filter is not a filter: %v", query)
	}

	if code, _, _ := run(t, server, "machine", "list", "--retired"); code != exit.OK {
		t.Fatal("code")
	}
	if f.requests[len(f.requests)-1].URL.Query().Get("retired") != "true" {
		t.Error("--retired asks for the retired ones as well")
	}
}

// A value outside a closed set is the instance's to refuse, and it arrives as
// exit 4 — the client keeps no second copy of the model.
func TestAValueOutsideAClosedSetIsExitFour(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 400, `{"type":"/problems/validation","title":"validation","status":400,
		"detail":"A machine is a vps, a dedicated, a vm or a local.","errors":{"kind":["Not a kind."]}}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "machine", "add", "ex44", "--kind", "toaster")
	if code != exit.Refused {
		t.Fatalf("code %d", code)
	}
	if out != "" || !strings.Contains(stderr, "vps") {
		t.Fatalf("stdout %q, stderr %q", out, stderr)
	}
}

func TestRecordUsageMistakesAreExitTwo(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, args := range [][]string{
		{"machine", "set", "ex44"},
		{"software", "set", "logaffe"},
		{"inst", "set", "logaffe-prod"},
		{"machine", "set", "ex44", "--measured-at", "last tuesday"},
		{"inst", "set", "logaffe-prod", "--port", "443"},
		{"inst", "set", "logaffe-prod", "--port", "https/tcp:public"},
		{"machine", "add", "ex44", "--kind", "vps", "--description", "one", "--description-file", "-"},
	} {
		if code, _, stderr := run(t, server, args...); code != exit.Usage || stderr == "" {
			t.Errorf("%v: code %d, stderr %q", args, code, stderr)
		}
	}
}

// What `view` prints for a person: the head, the fields that are filled in, and
// the description as it is stored.
func TestViewPrintsWhatIsFilledInAndNothingElse(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) {
		return 200, `{"key":"cx22","name":"cx22","hostname":null,"kind":"vps","host":null,"provider":"hetzner",
		"plan":null,"location":null,"os":null,"arch":null,"cpu":null,"memory":null,"disk":null,"ipv4":null,
		"ipv6":null,"private_ip":null,"ssh":null,"status":"planned","measured_at":null,"description":"",` +
			identities + `}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "machine", "view", "cx22")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	want := "cx22  cx22\nkind: vps  status: planned\nprovider: hetzner\n" +
		"updated: 2026-09-05T12:00:00Z by quiet-otter-42  author: maintainer\n"
	if out != want {
		t.Fatalf("a machine nobody has measured says nothing about measuring:\n%q", out)
	}
}

// An installation prints its ports the way a person writes them.
func TestAnInstallationPrintsItsPortsAsPeopleReadThem(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "inst", "view", "logaffe-prod")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}
	if !strings.Contains(out, "ports: 443/tcp:public, 5432/tcp:private") {
		t.Fatalf("ports read as a person writes them:\n%s", out)
	}
	if !strings.Contains(out, "backup: active  monitoring: external  logging: central") {
		t.Fatalf("the three decisions are on one line:\n%s", out)
	}
	if !strings.HasSuffix(out, "\nThe one people look at.\n") {
		t.Fatalf("the description is printed as it is stored:\n%q", out)
	}
}

// The history says who changed what, and the note that came with it.
func TestHistoryPrintsTheNoteBesideTheChange(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "machine", "history", "ex44")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	lines := strings.Split(strings.TrimSuffix(out, "\n"), "\n")
	if len(lines) != 2 {
		t.Fatalf("one line per change:\n%s", out)
	}
	// A row with neither value prints the field and stops there.
	if !strings.HasSuffix(lines[0], "created         (replacing the old one)") {
		t.Errorf("created: %q", lines[0])
	}
	if !strings.HasSuffix(lines[1], "os             Ubuntu 24.04 → Ubuntu 26.04 LTS  (dist-upgrade)") {
		t.Errorf("os: %q", lines[1])
	}
}

// `--json` prints the object as the API answered it, and nothing else on stdout.
func TestJsonPrintsTheObjectAsTheApiAnsweredIt(t *testing.T) {
	f := records()
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, out, stderr := run(t, server, "--json", "machine", "view", "ex44")
	if code != exit.OK || stderr != "" {
		t.Fatalf("code %d, stderr %q", code, stderr)
	}

	answered := map[string]any{}
	if err := json.Unmarshal([]byte(out), &answered); err != nil {
		t.Fatalf("stdout is not JSON: %v", err)
	}
	if answered["key"] != "ex44" || answered["disk"] != "2×512G NVMe ZFS mirror" {
		t.Errorf("answered = %v", answered)
	}
}
