// Package config is where ha learns which instance it talks to and as whom.
//
// Two questions, each answered through a ladder rather than by one variable
// (ADR 0005): the instance is `--url`, then HOSTINGAFFE_URL, then the instance
// this machine last signed in to; the token is HOSTINGAFFE_TOKEN, then the
// token file if one was chosen, then the keychain. The environment wins for
// the token because that is how an agent receives its own and how CI holds one.
//
// There is no project file and there are no projects (VISION 9): one instance
// holds one team's infrastructure, and every token reads all of it. What is on
// disk is this file — the instance, and the path of a token file where one was
// chosen — and it holds no credential of its own.
package config

import (
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"net/url"
	"os"
	"path/filepath"
	"strings"
)

// Environment variables, in one place because they are a contract with CI,
// with containers and with whatever harness starts an agent.
const (
	EnvURL          = "HOSTINGAFFE_URL"
	EnvToken        = "HOSTINGAFFE_TOKEN"
	EnvInsecureHTTP = "HOSTINGAFFE_INSECURE_HTTP"
	EnvConfig       = "HOSTINGAFFE_CONFIG"
)

// UsageError is a mistake in the environment or the arguments: exit 2.
type UsageError struct{ Message string }

func (e *UsageError) Error() string { return e.Message }

// File is the user's configuration as it is on disk: the instance `ha login`
// signed in to, and where the session went when it did not go into the
// keychain. It holds no token — a token file, where one was chosen, is its own
// file with its own permissions (ADR 0005).
type File struct {
	Instance  string `json:"instance,omitempty"`
	TokenFile string `json:"token_file,omitempty"`
}

// Path is where the configuration lives: $HOSTINGAFFE_CONFIG if it is set,
// otherwise $XDG_CONFIG_HOME/hostingaffe/config.json, otherwise
// ~/.config/hostingaffe/config.json.
func Path(getenv func(string) string) (string, error) {
	if getenv == nil {
		getenv = os.Getenv
	}
	if explicit := strings.TrimSpace(getenv(EnvConfig)); explicit != "" {
		return explicit, nil
	}
	if xdg := strings.TrimSpace(getenv("XDG_CONFIG_HOME")); xdg != "" {
		return filepath.Join(xdg, "hostingaffe", "config.json"), nil
	}

	home := strings.TrimSpace(getenv("HOME"))
	if home == "" {
		var err error
		if home, err = os.UserHomeDir(); err != nil {
			return "", &UsageError{fmt.Sprintf(
				"no home directory: set %s to say where the configuration lives.", EnvConfig)}
		}
	}

	return filepath.Join(home, ".config", "hostingaffe", "config.json"), nil
}

// Load reads the configuration. A file that is not there is an empty one: a
// machine that has never signed in is not a machine with a broken install.
func Load(path string) (File, error) {
	content, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return File{}, nil
	}
	if err != nil {
		return File{}, &UsageError{fmt.Sprintf("%s could not be read: %v", path, err)}
	}

	var file File
	if err := json.Unmarshal(content, &file); err != nil {
		return File{}, &UsageError{fmt.Sprintf("%s is not readable as configuration: %v", path, err)}
	}

	return file, nil
}

// Save writes it, readable by its owner and nobody else. The directory is made
// with the same intent: which instances a person works against is nobody
// else's business on a shared machine.
func Save(path string, file File) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
		return err
	}

	content, err := json.MarshalIndent(file, "", "  ")
	if err != nil {
		return err
	}

	return os.WriteFile(path, append(content, '\n'), 0o600)
}

// ReadTokenFile reads a token out of the file a person chose over the keychain,
// and refuses one anybody else on the machine can read. A file mode is the only
// protection a token in a file has, and shrugging at 0644 would be the quiet
// fallback to plaintext ADR 0005 refuses to make.
func ReadTokenFile(path string) (string, error) {
	info, err := os.Stat(path)
	if err != nil {
		return "", &UsageError{fmt.Sprintf("%s holds this machine's token and could not be read: %v", path, err)}
	}
	if mode := info.Mode().Perm(); mode&0o077 != 0 {
		return "", &UsageError{fmt.Sprintf(
			"%s is readable by others (mode %04o). A token in a file is protected by nothing else: `chmod 600 %s`.",
			path, mode, path)}
	}

	content, err := os.ReadFile(path)
	if err != nil {
		return "", &UsageError{fmt.Sprintf("%s could not be read: %v", path, err)}
	}

	token := strings.TrimSpace(string(content))
	if token == "" {
		return "", &UsageError{fmt.Sprintf("%s is empty: run `ha login --token-file %s`.", path, path)}
	}

	return token, nil
}

// WriteTokenFile writes one, readable by its owner and nobody else.
func WriteTokenFile(path, token string) error {
	if directory := filepath.Dir(path); directory != "" {
		if err := os.MkdirAll(directory, 0o700); err != nil {
			return err
		}
	}

	return os.WriteFile(path, []byte(token+"\n"), 0o600)
}

// CheckAddress refuses plain HTTP to anything but a loopback host (ADR 0006).
// A token over plain HTTP is a token in somebody's network log, and localhost
// is the one place that cannot be true. The override is explicit and is never
// inferred.
func CheckAddress(address string, allowPlainHTTP bool) error {
	parsed, err := url.Parse(address)
	if err != nil || !parsed.IsAbs() || parsed.Host == "" {
		return &UsageError{fmt.Sprintf(
			"%q is not an address: scheme and host, like https://hosting.example.com.", address)}
	}

	switch parsed.Scheme {
	case "https":
		return nil
	case "http":
		if allowPlainHTTP || IsLoopback(parsed.Hostname()) {
			return nil
		}
		return &UsageError{fmt.Sprintf(
			"%s is plain HTTP to a host that is not loopback, and a token over plain HTTP is a token in the network log. "+
				"Use https://, or pass --insecure-http (or %s=1) to say you mean it.", address, EnvInsecureHTTP)}
	default:
		return &UsageError{fmt.Sprintf("%q is neither http nor https.", address)}
	}
}

// IsLoopback is the whole of what "this machine" means here: the two names and
// the two address families, and nothing that merely resolves to one.
func IsLoopback(host string) bool {
	if host == "localhost" || strings.HasSuffix(host, ".localhost") {
		return true
	}
	if ip := net.ParseIP(strings.Trim(host, "[]")); ip != nil {
		return ip.IsLoopback()
	}

	return false
}
