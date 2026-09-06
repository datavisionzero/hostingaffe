package config

import (
	"strings"
	"testing"
)

func env(values map[string]string) func(string) string {
	return func(key string) string { return values[key] }
}

func TestLoadNeedsBothVariables(t *testing.T) {
	if _, err := Load(env(map[string]string{"HOSTINGAFFE_TOKEN": "t"})); err == nil || !strings.Contains(err.Error(), "HOSTINGAFFE_URL") {
		t.Fatalf("expected a usage error naming HOSTINGAFFE_URL, got %v", err)
	}
	if _, err := Load(env(map[string]string{"HOSTINGAFFE_URL": "https://x.example"})); err == nil || !strings.Contains(err.Error(), "HOSTINGAFFE_TOKEN") {
		t.Fatalf("expected a usage error naming HOSTINGAFFE_TOKEN, got %v", err)
	}
	if _, err := Load(env(map[string]string{"HOSTINGAFFE_URL": "x.example", "HOSTINGAFFE_TOKEN": "t"})); err == nil {
		t.Fatal("expected a usage error for a relative URL")
	}
}

// Whitespace around either value is the shell's, not the operator's intent.
func TestBothVariablesAreTrimmed(t *testing.T) {
	cfg, err := Load(env(map[string]string{"HOSTINGAFFE_URL": " https://x.example ", "HOSTINGAFFE_TOKEN": " t "}))
	if err != nil {
		t.Fatal(err)
	}
	if cfg.URL != "https://x.example" || cfg.Token != "t" {
		t.Fatalf("unexpected config %+v", cfg)
	}
}
