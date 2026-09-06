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

const me = `{"id":"0198e0c0-0000-7000-8000-000000000002","kind":"user","name":"maintainer","email":"maintainer@example.test","administrator":true,"owner":null,"token":{"prefix":"ha_abcd","created_at":"2026-09-02T14:00:00.000000Z"},"metadata":null,"metadata_reported_at":null}`

// An instance that has neither the project nor an opinion about anything else.
func emptyInstance(t *testing.T) (*fake, *httptest.Server) {
	t.Helper()
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch {
		case r.URL.Path == "/version":
			return 200, `{"version":"0.0.0-dev"}`
		case r.URL.Path == "/me":
			return 200, me
		case r.Method == http.MethodGet && strings.HasPrefix(r.URL.Path, "/projects/"):
			return 404, `{"type":"/problems/not-found","status":404,"detail":"No project."}`
		case r.Method == http.MethodPost && r.URL.Path == "/projects":
			return 201, project
		default:
			return 404, `{"type":"/problems/not-found","status":404}`
		}
	}}
	return f, httptest.NewServer(f.handler())
}

func TestInitCreatesTheProjectAndWritesTheProjectFile(t *testing.T) {
	f, server := emptyInstance(t)
	defer server.Close()
	dir := t.TempDir()

	code, out, errOut := run(t, server, dir, "init", "plan")
	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
	if !strings.Contains(out, "PLAN · hostingaffe (created)") || !strings.Contains(out, "maintainer (user)") {
		t.Fatalf("unexpected output:\n%s", out)
	}

	written, err := os.ReadFile(filepath.Join(dir, ".hostingaffe"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(written), "project = PLAN") {
		t.Errorf(".hostingaffe = %q", written)
	}

	var body map[string]any
	if err := json.Unmarshal([]byte(f.bodies[len(f.bodies)-1]), &body); err != nil {
		t.Fatal(err)
	}
	if body["key"] != "PLAN" || body["name"] != filepath.Base(dir) {
		t.Errorf("created %v; the name defaults to the directory", body)
	}
}

// Running init twice, or against a project a colleague made, is the ordinary
// case rather than a mistake.
func TestInitTakesAProjectThatIsAlreadyThereAndGuardsTheProjectFile(t *testing.T) {
	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch r.URL.Path {
		case "/version":
			return 200, `{"version":"0.0.0-dev"}`
		case "/me":
			return 200, me
		default:
			return 200, project
		}
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()
	dir := repository(t, "project = OLD\n")

	code, _, errOut := run(t, server, dir, "init", "PLAN")
	if code != exit.Usage || !strings.Contains(errOut, "--force") {
		t.Fatalf("code %d, stderr %q; an existing project file is not overwritten silently", code, errOut)
	}

	code, out, errOut := run(t, server, dir, "init", "PLAN", "--force")
	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
	if !strings.Contains(out, "PLAN · hostingaffe (already there)") {
		t.Fatalf("unexpected output:\n%s", out)
	}
	if written, _ := os.ReadFile(filepath.Join(dir, ".hostingaffe")); !strings.Contains(string(written), "project = PLAN") {
		t.Errorf(".hostingaffe = %q", written)
	}
	for _, r := range f.requests {
		if r.Method == http.MethodPost && r.URL.Path == "/projects" {
			t.Error("a project that is already there is taken, not created again")
		}
	}
}

// The first mistake a newcomer makes is one of these two, and which one it was
// is the whole answer.
func TestInitSaysWhetherTheAddressOrTheTokenWasWrong(t *testing.T) {
	unreachable := httptest.NewServer(http.HandlerFunc(func(http.ResponseWriter, *http.Request) {}))
	unreachable.Close()
	code, _, errOut := run(t, unreachable, t.TempDir(), "init", "PLAN")
	if code != exit.Unreachable || !strings.Contains(errOut, "HOSTINGAFFE_URL") {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}

	f := &fake{t: t, version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Path == "/version" {
			return 200, `{"version":"0.0.0-dev"}`
		}
		return 401, `{"type":"/problems/unauthenticated","status":401}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, errOut = run(t, server, t.TempDir(), "init", "PLAN")
	if code != exit.Denied || !strings.Contains(errOut, "HOSTINGAFFE_TOKEN") {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
}

func TestInitProposesTheKeyFromTheDirectoryAndSaysSoWhenItCannot(t *testing.T) {
	_, server := emptyInstance(t)
	defer server.Close()

	dir := filepath.Join(t.TempDir(), "hostingaffe")
	if err := os.Mkdir(dir, 0o755); err != nil {
		t.Fatal(err)
	}
	code, out, errOut := run(t, server, dir, "init")
	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
	if !strings.Contains(out, "PLAN") {
		t.Fatalf("unexpected output:\n%s", out)
	}

	// Nothing is asked: what is missing is named (VISION 6.1).
	awkward := filepath.Join(t.TempDir(), "-")
	if err := os.Mkdir(awkward, 0o755); err != nil {
		t.Fatal(err)
	}
	code, _, errOut = run(t, server, awkward, "init")
	if code != exit.Usage || !strings.Contains(errOut, "ha init KEY") {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}
}

func TestInitJsonSaysWhatItDid(t *testing.T) {
	_, server := emptyInstance(t)
	defer server.Close()

	code, out, errOut := run(t, server, t.TempDir(), "init", "PLAN", "--json")
	if code != exit.OK || errOut != "" {
		t.Fatalf("code %d, stderr %q", code, errOut)
	}

	var result struct {
		URL     string `json:"url"`
		Project struct {
			Key     string `json:"key"`
			Created bool   `json:"created"`
		} `json:"project"`
		File string   `json:"file"`
		Next []string `json:"next"`
	}
	if err := json.Unmarshal([]byte(out), &result); err != nil {
		t.Fatal(err)
	}
	if result.URL != server.URL || result.Project.Key != "PLAN" || !result.Project.Created || result.File == "" || len(result.Next) != 2 {
		t.Fatalf("unexpected result: %s", out)
	}
}
