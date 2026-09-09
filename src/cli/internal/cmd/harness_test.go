package cmd

import (
	"bytes"
	"context"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/keychain"
)

// fake is an instance as far as a command test needs one: it records what it
// was asked and answers what the test says.
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

func run(t *testing.T, server *httptest.Server, args ...string) (code int, stdout, stderr string) {
	t.Helper()
	var out, errOut bytes.Buffer
	code = Run(context.Background(), args, Env{
		Getenv: environment(t, map[string]string{
			"HOSTINGAFFE_URL":   server.URL,
			"HOSTINGAFFE_TOKEN": "ha_test-token-of-thirty-two-characters-or-more",
		}),
		Stdin:    strings.NewReader(""),
		Stdout:   &out,
		Stderr:   &errOut,
		HTTP:     server.Client(),
		Keychain: &memory{},
	})
	return code, out.String(), errOut.String()
}

// environment is what a test's ha runs in: the values the test names, plus a
// configuration of its own, so that nothing reaches around into the
// configuration of whoever is running the tests.
func environment(t *testing.T, values map[string]string) func(string) string {
	t.Helper()
	full := map[string]string{"HOSTINGAFFE_CONFIG": filepath.Join(t.TempDir(), "config.json")}
	for key, value := range values {
		full[key] = value
	}
	return func(key string) string { return full[key] }
}

// memory is a keychain that is one map, so that a test never touches the
// machine's own store and CI needs no Secret Service (ADR 0005).
type memory struct {
	entries map[string]string
	broken  bool
}

func (m *memory) Store(instance, token string) error {
	if m.broken {
		return keychain.ErrUnavailable
	}
	if m.entries == nil {
		m.entries = map[string]string{}
	}
	m.entries[instance] = token
	return nil
}

func (m *memory) Read(instance string) (string, error) {
	if m.broken {
		return "", keychain.ErrUnavailable
	}
	if token, held := m.entries[instance]; held {
		return token, nil
	}
	return "", keychain.ErrNotFound
}

func (m *memory) Forget(instance string) error {
	if m.broken {
		return keychain.ErrUnavailable
	}
	delete(m.entries, instance)
	return nil
}

// An argument mistake is exit 2 wherever it happens, like a flag mistake:
// exit 1 is a bug in ha, and typing too few arguments is not one (docs/cli.md,
// Exit codes).
func TestAnArgumentMistakeIsExitTwo(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, "{}" }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	for _, args := range [][]string{
		{"machine", "view"},
		{"machine", "view", "ex44", "and-another"},
		{"search"},
		{"deploy", "view", "logaffe-prod"},
		{"machine", "invent"},
		{"files", "invent"},
	} {
		if code, _, stderr := run(t, server, args...); code != 2 || stderr == "" {
			t.Errorf("%v: code %d, stderr %q", args, code, stderr)
		}
	}
}
