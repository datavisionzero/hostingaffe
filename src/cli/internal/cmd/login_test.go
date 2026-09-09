package cmd

import (
	"bytes"
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// A login as it goes: the instance begins one, ha prints the code and polls,
// and the token it collects lands in the keychain rather than in the terminal
// (ADR 0005).
func TestALoginPrintsTheCodePollsAndKeepsTheTokenInTheKeychain(t *testing.T) {
	polls := 0
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch r.URL.Path {
		case "/api/device/logins":
			return 200, `{"device_code":"the-device-code","user_code":"BCDF-GHJK","verification_uri":"/device",
				"verification_uri_complete":"/device?code=BCDF-GHJK","expires_in_seconds":600,"interval_seconds":1}`
		case "/api/device/tokens":
			polls++
			if polls == 1 {
				return 400, `{"type":"/problems/device-pending","title":"Nobody has approved this login yet","status":400}`
			}
			return 200, `{"user":{"id":"11111111-1111-1111-1111-111111111111","kind":"user","name":"maintainer"},
				"token":{"id":"22222222-2222-2222-2222-222222222222","prefix":"ha_abcde","secret":"ha_the-collected-secret","created_at":"2026-09-09T10:00:00Z"}}`
		}
		t.Fatalf("unexpected request %s", r.URL.Path)
		return 500, ""
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	store := &memory{}
	configPath := filepath.Join(t.TempDir(), "config.json")
	code, stdout, stderr := runWith(t, server, store, map[string]string{"HOSTINGAFFE_CONFIG": configPath, "HOSTINGAFFE_URL": server.URL}, "login")

	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	if !strings.Contains(stderr, "BCDF-GHJK") || !strings.Contains(stderr, server.URL+"/device") {
		t.Fatalf("the code and where to enter it belong on stderr: %s", stderr)
	}
	if !strings.Contains(stderr, "Signed in to "+server.URL+" as maintainer.") {
		t.Fatalf("expected the sign-in to be said: %s", stderr)
	}
	if polls != 2 {
		t.Fatalf("expected a second poll after `device-pending`, made %d", polls)
	}

	// The secret is in the store and in no output.
	if store.entries[server.URL] != "ha_the-collected-secret" {
		t.Fatalf("the token should be in the keychain, got %q", store.entries[server.URL])
	}
	if strings.Contains(stdout+stderr, "ha_the-collected-secret") {
		t.Fatalf("the secret must not reach the terminal: %s %s", stdout, stderr)
	}

	// And the instance is remembered, without the credential.
	written, err := os.ReadFile(configPath)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(written), server.URL) || strings.Contains(string(written), "ha_") {
		t.Fatalf("the configuration says which instance and holds no token: %s", written)
	}
}

// The next command finds the session without being told: no variable, no flag.
func TestACommandAfterALoginFindsTheInstanceAndTheTokenOnItsOwn(t *testing.T) {
	var authorization string
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		authorization = r.Header.Get("Authorization")
		return 200, `{"id":"11111111-1111-1111-1111-111111111111","kind":"user","name":"maintainer","administrator":true,
			"token":{"id":"22222222-2222-2222-2222-222222222222","prefix":"ha_abcde","created_at":"2026-09-09T10:00:00Z"}}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	configPath := filepath.Join(t.TempDir(), "config.json")
	if err := os.WriteFile(configPath, []byte(`{"instance":"`+server.URL+`"}`), 0o600); err != nil {
		t.Fatal(err)
	}
	store := &memory{entries: map[string]string{server.URL: "ha_kept-in-the-keychain"}}

	code, _, stderr := runWith(t, server, store, map[string]string{"HOSTINGAFFE_CONFIG": configPath}, "me")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	if authorization != "Bearer ha_kept-in-the-keychain" {
		t.Fatalf("expected the keychain's token, got %q", authorization)
	}
}

// A machine with no keychain is told the two ways on, and nothing is written.
func TestNoKeychainIsSaidRatherThanQuietlyWrittenToAFile(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Path == "/api/device/logins" {
			return 200, `{"device_code":"the-device-code","user_code":"BCDF-GHJK","verification_uri":"/device",
				"verification_uri_complete":"","expires_in_seconds":600,"interval_seconds":0}`
		}
		return 200, `{"user":{"id":"11111111-1111-1111-1111-111111111111","kind":"user","name":"maintainer"},
			"token":{"id":"22222222-2222-2222-2222-222222222222","prefix":"ha_abcde","secret":"ha_the-collected-secret","created_at":"2026-09-09T10:00:00Z"}}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := runWith(t, server, &memory{broken: true}, map[string]string{"HOSTINGAFFE_URL": server.URL}, "login")
	if code != 2 {
		t.Fatalf("expected exit 2, got %d: %s", code, stderr)
	}
	if !strings.Contains(stderr, "HOSTINGAFFE_TOKEN") || !strings.Contains(stderr, "--token-file") {
		t.Fatalf("the refusal names the two ways on: %s", stderr)
	}
}

// `--token-file` is the other way on, and what it writes is readable by its
// owner and nobody else.
func TestATokenFileIsWrittenWhenItWasAskedForAndReadBackAfterwards(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		switch r.URL.Path {
		case "/api/device/logins":
			return 200, `{"device_code":"the-device-code","user_code":"BCDF-GHJK","verification_uri":"/device",
				"verification_uri_complete":"","expires_in_seconds":600,"interval_seconds":0}`
		case "/api/device/tokens":
			return 200, `{"user":{"id":"11111111-1111-1111-1111-111111111111","kind":"user","name":"maintainer"},
				"token":{"id":"22222222-2222-2222-2222-222222222222","prefix":"ha_abcde","secret":"ha_in-the-file","created_at":"2026-09-09T10:00:00Z"}}`
		}
		return 200, `{"id":"11111111-1111-1111-1111-111111111111","kind":"user","name":"maintainer","administrator":false,
			"token":{"id":"22222222-2222-2222-2222-222222222222","prefix":"ha_abcde","created_at":"2026-09-09T10:00:00Z"}}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	directory := t.TempDir()
	tokenFile := filepath.Join(directory, "token")
	values := map[string]string{
		"HOSTINGAFFE_CONFIG": filepath.Join(directory, "config.json"),
		"HOSTINGAFFE_URL":    server.URL,
	}

	if code, _, stderr := runWith(t, server, &memory{}, values, "login", "--token-file", tokenFile); code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}

	info, err := os.Stat(tokenFile)
	if err != nil {
		t.Fatal(err)
	}
	if mode := info.Mode().Perm(); mode != 0o600 {
		t.Fatalf("expected mode 0600, got %04o", mode)
	}

	// And the token file is what the next command reads, without a variable.
	var authorization string
	f.answer = func(r *http.Request) (int, string) {
		authorization = r.Header.Get("Authorization")
		return 200, `{"id":"11111111-1111-1111-1111-111111111111","kind":"user","name":"maintainer","administrator":false,
			"token":{"id":"22222222-2222-2222-2222-222222222222","prefix":"ha_abcde","created_at":"2026-09-09T10:00:00Z"}}`
	}
	delete(values, "HOSTINGAFFE_URL")
	if code, _, stderr := runWith(t, server, &memory{}, values, "me"); code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	if authorization != "Bearer ha_in-the-file" {
		t.Fatalf("expected the token file's token, got %q", authorization)
	}
}

// A logout revokes the token it is holding and forgets it here.
func TestALogoutRevokesThisMachinesTokenAndForgetsIt(t *testing.T) {
	var revoked string
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.Method == http.MethodDelete {
			revoked = r.URL.Path
			return 204, ""
		}
		return 200, `{"id":"11111111-1111-1111-1111-111111111111","kind":"user","name":"maintainer","administrator":false,
			"token":{"id":"22222222-2222-2222-2222-222222222222","prefix":"ha_abcde","created_at":"2026-09-09T10:00:00Z"}}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	configPath := filepath.Join(t.TempDir(), "config.json")
	if err := os.WriteFile(configPath, []byte(`{"instance":"`+server.URL+`"}`), 0o600); err != nil {
		t.Fatal(err)
	}
	store := &memory{entries: map[string]string{server.URL: "ha_kept-in-the-keychain"}}

	code, _, stderr := runWith(t, server, store, map[string]string{"HOSTINGAFFE_CONFIG": configPath}, "logout")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	if revoked != "/api/tokens/22222222-2222-2222-2222-222222222222" {
		t.Fatalf("expected the token it came in under to be revoked, got %q", revoked)
	}
	if _, held := store.entries[server.URL]; held {
		t.Fatal("the keychain should no longer hold this instance's token")
	}
}

// A token out of the environment is the agent's or CI's, and ha did not put it
// there: a logout that revoked it would revoke something the person at this
// terminal may not know they are holding.
func TestALogoutRefusesATokenItDidNotPutThere(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { return 200, "{}" }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := runWith(t, server, &memory{}, map[string]string{
		"HOSTINGAFFE_URL":   server.URL,
		"HOSTINGAFFE_TOKEN": "ha_from-the-environment",
	}, "logout")

	if code != 2 {
		t.Fatalf("expected exit 2, got %d: %s", code, stderr)
	}
	if !strings.Contains(stderr, "HOSTINGAFFE_TOKEN") {
		t.Fatalf("the refusal names the variable: %s", stderr)
	}
}

// `status` is the command somebody runs because something is wrong: one broken
// answer must not take the other one down with it.
func TestStatusSaysWhichInstanceAsWhomAndWhereTheAnswerCameFrom(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Path == "/api/version" {
			return 200, `{"version":"0.0.0-dev"}`
		}
		return 200, `{"id":"11111111-1111-1111-1111-111111111111","kind":"user","name":"maintainer","administrator":true,
			"token":{"id":"22222222-2222-2222-2222-222222222222","prefix":"ha_abcde","created_at":"2026-09-09T10:00:00Z"}}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	store := &memory{entries: map[string]string{server.URL: "ha_kept-in-the-keychain"}}
	configPath := filepath.Join(t.TempDir(), "config.json")
	if err := os.WriteFile(configPath, []byte(`{"instance":"`+server.URL+`"}`), 0o600); err != nil {
		t.Fatal(err)
	}

	code, stdout, stderr := runWith(t, server, store, map[string]string{"HOSTINGAFFE_CONFIG": configPath}, "status")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}
	for _, expected := range []string{server.URL, "the keychain", "maintainer", "administrator"} {
		if !strings.Contains(stdout, expected) {
			t.Fatalf("expected %q in the status, got %s", expected, stdout)
		}
	}

	// With no token at all it still says which instance this is, and why the
	// other half is missing.
	code, stdout, _ = runWith(t, server, &memory{}, map[string]string{"HOSTINGAFFE_CONFIG": configPath}, "status")
	if code != 0 {
		t.Fatalf("status should not fail on a missing token, exit %d", code)
	}
	if !strings.Contains(stdout, "none:") || !strings.Contains(stdout, "ha login") {
		t.Fatalf("expected the missing token to be named, got %s", stdout)
	}
}

// A token over plain HTTP is a token in somebody's network log (ADR 0006).
func TestPlainHTTPOffLoopbackIsRefusedBeforeARequestGoesOut(t *testing.T) {
	asked := false
	f := &fake{version: "0.0.0-dev", answer: func(*http.Request) (int, string) { asked = true; return 200, "{}" }}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, _, stderr := runWith(t, server, &memory{}, map[string]string{
		"HOSTINGAFFE_URL":   "http://hosting.example.com",
		"HOSTINGAFFE_TOKEN": "ha_test-token-of-thirty-two-characters-or-more",
	}, "me")

	if code != 2 {
		t.Fatalf("expected exit 2, got %d: %s", code, stderr)
	}
	if asked {
		t.Fatal("nothing should have been sent")
	}
	if !strings.Contains(stderr, "--insecure-http") {
		t.Fatalf("the refusal names the override: %s", stderr)
	}
}

func TestJSONLoginPrintsTheIdentityAndNeverTheSecret(t *testing.T) {
	f := &fake{version: "0.0.0-dev", answer: func(r *http.Request) (int, string) {
		if r.URL.Path == "/api/device/logins" {
			return 200, `{"device_code":"the-device-code","user_code":"BCDF-GHJK","verification_uri":"/device",
				"verification_uri_complete":"","expires_in_seconds":600,"interval_seconds":0}`
		}
		return 200, `{"user":{"id":"11111111-1111-1111-1111-111111111111","kind":"user","name":"maintainer"},
			"token":{"id":"22222222-2222-2222-2222-222222222222","prefix":"ha_abcde","secret":"ha_never-printed","created_at":"2026-09-09T10:00:00Z"}}`
	}}
	server := httptest.NewServer(f.handler())
	defer server.Close()

	code, stdout, stderr := runWith(t, server, &memory{}, map[string]string{"HOSTINGAFFE_URL": server.URL}, "login", "--json")
	if code != 0 {
		t.Fatalf("exit %d: %s", code, stderr)
	}

	var answered map[string]any
	if err := json.Unmarshal([]byte(stdout), &answered); err != nil {
		t.Fatalf("stdout is not the object it promises: %v (%s)", err, stdout)
	}
	if strings.Contains(stdout, "ha_never-printed") {
		t.Fatalf("the secret must not be in --json either: %s", stdout)
	}
}

func runWith(t *testing.T, server *httptest.Server, store Keychain, values map[string]string, args ...string) (int, string, string) {
	t.Helper()
	var out, errOut bytes.Buffer
	code := Run(context.Background(), args, Env{
		Getenv:   environment(t, values),
		Stdin:    strings.NewReader(""),
		Stdout:   &out,
		Stderr:   &errOut,
		HTTP:     server.Client(),
		Keychain: store,
	})
	return code, out.String(), errOut.String()
}
