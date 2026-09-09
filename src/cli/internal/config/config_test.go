package config

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func env(values map[string]string) func(string) string {
	return func(key string) string { return values[key] }
}

// The instance is the flag, then the variable, then what this machine signed
// in to. Nothing else, and in that order (ADR 0005).
func TestTheAddressLadderIsFlagThenVariableThenTheSignedInInstance(t *testing.T) {
	in := Input{
		Getenv:  env(map[string]string{EnvURL: "https://variable.example"}),
		File:    File{Instance: "https://signed-in.example"},
		Address: "https://flag.example",
	}

	address, err := in.ResolveAddress()
	if err != nil || address != "https://flag.example" {
		t.Fatalf("expected the flag, got %q (%v)", address, err)
	}

	in.Address = ""
	if address, _ := in.ResolveAddress(); address != "https://variable.example" {
		t.Fatalf("expected the variable, got %q", address)
	}

	in.Getenv = env(nil)
	if address, _ := in.ResolveAddress(); address != "https://signed-in.example" {
		t.Fatalf("expected the signed-in instance, got %q", address)
	}

	in.File = File{}
	if _, err := in.ResolveAddress(); err == nil || !strings.Contains(err.Error(), "ha login") {
		t.Fatalf("expected a usage error naming `ha login`, got %v", err)
	}
}

// The environment wins, because that is how an agent receives its own token
// and how CI holds one.
func TestTheTokenLadderIsTheVariableThenTheFileThenTheKeychain(t *testing.T) {
	path := filepath.Join(t.TempDir(), "token")
	if err := WriteTokenFile(path, "ha_from-the-file"); err != nil {
		t.Fatal(err)
	}

	in := Input{
		Getenv:       env(map[string]string{EnvToken: " ha_from-the-environment "}),
		File:         File{TokenFile: path},
		ReadKeychain: func(string) (string, error) { return "ha_from-the-keychain", nil },
	}

	token, from, err := in.ResolveToken("https://hosting.example.test")
	if err != nil || token != "ha_from-the-environment" || from != EnvToken {
		t.Fatalf("expected the environment, got %q from %q (%v)", token, from, err)
	}

	in.Getenv = env(nil)
	if token, from, _ := in.ResolveToken("https://hosting.example.test"); token != "ha_from-the-file" || from != path {
		t.Fatalf("expected the token file, got %q from %q", token, from)
	}

	in.File = File{}
	if token, from, _ := in.ResolveToken("https://hosting.example.test"); token != "ha_from-the-keychain" || from != "the keychain" {
		t.Fatalf("expected the keychain, got %q from %q", token, from)
	}

	in.ReadKeychain = nil
	if _, _, err := in.ResolveToken("https://hosting.example.test"); err == nil || !strings.Contains(err.Error(), "ha login") {
		t.Fatalf("expected a usage error naming `ha login`, got %v", err)
	}
}

// A file mode is the only protection a token in a file has.
func TestATokenFileOthersCanReadIsRefusedWithTheChmodThatFixesIt(t *testing.T) {
	path := filepath.Join(t.TempDir(), "token")
	if err := WriteTokenFile(path, "ha_a-token"); err != nil {
		t.Fatal(err)
	}
	if err := os.Chmod(path, 0o644); err != nil {
		t.Fatal(err)
	}

	_, err := ReadTokenFile(path)
	if err == nil || !strings.Contains(err.Error(), "chmod 600") {
		t.Fatalf("expected the chmod that fixes it, got %v", err)
	}
}

func TestATokenFileIsWrittenReadableOnlyByItsOwner(t *testing.T) {
	path := filepath.Join(t.TempDir(), "token")
	if err := WriteTokenFile(path, "ha_a-token"); err != nil {
		t.Fatal(err)
	}

	info, err := os.Stat(path)
	if err != nil {
		t.Fatal(err)
	}
	if mode := info.Mode().Perm(); mode != 0o600 {
		t.Fatalf("expected mode 0600, got %04o", mode)
	}

	token, err := ReadTokenFile(path)
	if err != nil || token != "ha_a-token" {
		t.Fatalf("expected the token back, got %q (%v)", token, err)
	}
}

// A token over plain HTTP is a token in somebody's network log; loopback is
// the one place that cannot be true (ADR 0006).
func TestPlainHTTPIsRefusedOffLoopbackAndAllowedOnIt(t *testing.T) {
	for _, address := range []string{"http://localhost:8080", "http://127.0.0.1:8080", "http://[::1]:8080", "http://api.localhost", "https://hosting.example.com"} {
		if err := CheckAddress(address, false); err != nil {
			t.Fatalf("%s should be fine: %v", address, err)
		}
	}

	err := CheckAddress("http://hosting.example.com", false)
	if err == nil || !strings.Contains(err.Error(), "--insecure-http") {
		t.Fatalf("expected the refusal to name the override, got %v", err)
	}
	if err := CheckAddress("http://hosting.example.com", true); err != nil {
		t.Fatalf("the override should allow it: %v", err)
	}

	for _, address := range []string{"hosting.example.com", "ftp://hosting.example.com", ""} {
		if err := CheckAddress(address, true); err == nil {
			t.Fatalf("%q is not an address ha talks to", address)
		}
	}
}

func TestTheOverrideIsTheFlagOrTheVariableAndNothingInferred(t *testing.T) {
	for value, allowed := range map[string]bool{"1": true, "true": true, "yes": true, "": false, "0": false, "maybe": false} {
		in := Input{Getenv: env(map[string]string{EnvInsecureHTTP: value})}
		if in.AllowsPlainHTTP() != allowed {
			t.Fatalf("%s=%q should be %v", EnvInsecureHTTP, value, allowed)
		}
	}
}

func TestTheConfigurationLivesWhereTheEnvironmentSaysAndCarriesNoToken(t *testing.T) {
	directory := t.TempDir()

	explicit := filepath.Join(directory, "explicit.json")
	if path, _ := Path(env(map[string]string{EnvConfig: explicit})); path != explicit {
		t.Fatalf("expected %s, got %s", explicit, path)
	}
	if path, _ := Path(env(map[string]string{"XDG_CONFIG_HOME": directory})); path != filepath.Join(directory, "hostingaffe", "config.json") {
		t.Fatalf("unexpected XDG path %s", path)
	}
	if path, _ := Path(env(map[string]string{"HOME": directory})); path != filepath.Join(directory, ".config", "hostingaffe", "config.json") {
		t.Fatalf("unexpected home path %s", path)
	}

	// A machine that has never signed in is not a machine with a broken
	// installation: no file is an empty one.
	path := filepath.Join(directory, "hostingaffe", "config.json")
	file, err := Load(path)
	if err != nil || file != (File{}) {
		t.Fatalf("expected an empty configuration, got %+v (%v)", file, err)
	}

	if err := Save(path, File{Instance: "https://hosting.example.test"}); err != nil {
		t.Fatal(err)
	}
	written, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(written), "ha_") || strings.Contains(string(written), "token\"") {
		t.Fatalf("the configuration holds no credential: %s", written)
	}
	if again, _ := Load(path); again.Instance != "https://hosting.example.test" {
		t.Fatalf("expected the instance back, got %+v", again)
	}
}
