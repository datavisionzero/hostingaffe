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

func TestLoadNeedsBothVariables(t *testing.T) {
	if _, err := Load(env(map[string]string{"HOSTINGAFFE_TOKEN": "t"}), t.TempDir()); err == nil || !strings.Contains(err.Error(), "HOSTINGAFFE_URL") {
		t.Fatalf("expected a usage error naming HOSTINGAFFE_URL, got %v", err)
	}
	if _, err := Load(env(map[string]string{"HOSTINGAFFE_URL": "https://x.example"}), t.TempDir()); err == nil || !strings.Contains(err.Error(), "HOSTINGAFFE_TOKEN") {
		t.Fatalf("expected a usage error naming HOSTINGAFFE_TOKEN, got %v", err)
	}
	if _, err := Load(env(map[string]string{"HOSTINGAFFE_URL": "x.example", "HOSTINGAFFE_TOKEN": "t"}), t.TempDir()); err == nil {
		t.Fatal("expected a usage error for a relative URL")
	}
}

func TestProjectFileIsFoundUpwardsAndParsed(t *testing.T) {
	root := t.TempDir()
	if err := os.WriteFile(filepath.Join(root, FileName), []byte("# the project of this repository\nproject = plan\n"), 0o644); err != nil {
		t.Fatal(err)
	}
	nested := filepath.Join(root, "src", "deep")
	if err := os.MkdirAll(nested, 0o755); err != nil {
		t.Fatal(err)
	}

	cfg, err := Load(env(map[string]string{"HOSTINGAFFE_URL": "https://x.example", "HOSTINGAFFE_TOKEN": "t"}), nested)
	if err != nil {
		t.Fatal(err)
	}
	if cfg.Project != "PLAN" || cfg.File != filepath.Join(root, FileName) {
		t.Fatalf("unexpected config %+v", cfg)
	}
}

func TestProjectFileRefusesWhatItDoesNotKnow(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, FileName), []byte("projekt = PLAN\n"), 0o644); err != nil {
		t.Fatal(err)
	}
	_, err := Load(env(map[string]string{"HOSTINGAFFE_URL": "https://x.example", "HOSTINGAFFE_TOKEN": "t"}), dir)
	if err == nil || !strings.Contains(err.Error(), "unknown key") {
		t.Fatalf("expected an unknown-key error, got %v", err)
	}

	if err := os.WriteFile(filepath.Join(dir, FileName), []byte("projekt = x\n"), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := Load(env(map[string]string{"HOSTINGAFFE_URL": "https://x.example", "HOSTINGAFFE_TOKEN": "t"}), dir); err == nil {
		t.Fatal("expected an error for a file without a project")
	}
}

func TestWithoutAFileThereIsNoProject(t *testing.T) {
	cfg, err := Load(env(map[string]string{"HOSTINGAFFE_URL": "https://x.example", "HOSTINGAFFE_TOKEN": "t"}), t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	if cfg.Project != "" || cfg.File != "" {
		t.Fatalf("unexpected config %+v", cfg)
	}
}
