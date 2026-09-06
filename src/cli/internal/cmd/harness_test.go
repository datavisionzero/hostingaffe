package cmd

import (
	"bytes"
	"context"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
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
		Getenv: func(k string) string {
			return map[string]string{"HOSTINGAFFE_URL": server.URL, "HOSTINGAFFE_TOKEN": "ha_test-token-of-thirty-two-characters-or-more"}[k]
		},
		Stdin:  strings.NewReader(""),
		Stdout: &out,
		Stderr: &errOut,
		HTTP:   server.Client(),
	})
	return code, out.String(), errOut.String()
}
