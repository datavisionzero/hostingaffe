// Package config is where ha learns which instance it talks to and as whom:
// two environment variables, and nothing else. There is no project file and no
// project (VISION 6.1, 9) — one instance holds one team's infrastructure, and
// every token reads all of it.
package config

import (
	"fmt"
	"net/url"
	"strings"
)

// Config is what a command runs with.
type Config struct {
	// URL is the instance, from HOSTINGAFFE_URL.
	URL string
	// Token is the caller's token, from HOSTINGAFFE_TOKEN; the server tells a user
	// token from an agent token, ha never says which it holds (ADR 0015).
	Token string
}

// UsageError is a mistake in the environment or the arguments: exit 2.
type UsageError struct{ Message string }

func (e *UsageError) Error() string { return e.Message }

// Load reads the environment. Every value can be overridden by a flag; that is
// the caller's, after Load.
func Load(getenv func(string) string) (Config, error) {
	cfg := Config{URL: strings.TrimSpace(getenv("HOSTINGAFFE_URL")), Token: strings.TrimSpace(getenv("HOSTINGAFFE_TOKEN"))}

	if cfg.URL == "" {
		return cfg, &UsageError{"HOSTINGAFFE_URL is not set: the address of the instance, scheme and host."}
	}
	if u, err := url.Parse(cfg.URL); err != nil || !u.IsAbs() || (u.Scheme != "http" && u.Scheme != "https") {
		return cfg, &UsageError{fmt.Sprintf("HOSTINGAFFE_URL is %q; it has to be an absolute http or https address.", cfg.URL)}
	}
	if cfg.Token == "" {
		return cfg, &UsageError{"HOSTINGAFFE_TOKEN is not set: a user token or an agent token."}
	}

	return cfg, nil
}
