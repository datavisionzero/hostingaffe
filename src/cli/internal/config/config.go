// Package config is where ha learns which instance it talks to and as whom:
// two environment variables, and an optional .hostingaffe file in the repository
// that fixes the project (VISION 6.1, 13).
package config

import (
	"bufio"
	"errors"
	"fmt"
	"net/url"
	"os"
	"path/filepath"
	"strings"
)

// FileName is the project file: checked in at the root of a repository, it
// points from that repository at exactly one project and, where one project
// spans several repositories, names the `repo` label of this one (CONTEXT.md,
// Project file).
const FileName = ".hostingaffe"

// Config is what a command runs with.
type Config struct {
	// URL is the instance, from HOSTINGAFFE_URL.
	URL string
	// Token is the caller's token, from HOSTINGAFFE_TOKEN; the server tells a user
	// token from an agent token, ha never says which it holds (ADR 0015).
	Token string
	// Project is the project key, from the file or from --project.
	Project string
	// Repo is the `repo` label of this repository, from the file, or empty.
	Repo string
	// File is where the project file was found, or empty.
	File string
}

// UsageError is a mistake in the environment or the arguments: exit 2.
type UsageError struct{ Message string }

func (e *UsageError) Error() string { return e.Message }

// Load reads the environment and looks for the project file from dir upwards.
// Every value the file sets can be overridden by a flag; that is the caller's,
// after Load.
func Load(getenv func(string) string, dir string) (Config, error) {
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

	path, found := find(dir)
	if found {
		file, err := parse(path)
		if err != nil {
			return cfg, &UsageError{fmt.Sprintf("%s: %v", path, err)}
		}
		cfg.File = path
		cfg.Project = file.project
		cfg.Repo = file.repo
	}

	return cfg, nil
}

type projectFile struct {
	project string
	repo    string
}

// find walks up from dir to the root, the way git finds its own directory.
func find(dir string) (string, bool) {
	for {
		candidate := filepath.Join(dir, FileName)
		if info, err := os.Stat(candidate); err == nil && !info.IsDir() {
			return candidate, true
		}

		parent := filepath.Dir(dir)
		if parent == dir {
			return "", false
		}
		dir = parent
	}
}

// parse reads `key = value` lines; `#` starts a comment. Two keys are known,
// `project` and `repo`, and anything else is a mistake rather than ignored — a
// misspelt `projekt` that silently did nothing would send every command to the
// wrong project.
func parse(path string) (projectFile, error) {
	f, err := os.Open(path)
	if err != nil {
		return projectFile{}, err
	}
	defer f.Close()

	var file projectFile
	scanner := bufio.NewScanner(f)
	line := 0
	for scanner.Scan() {
		line++
		text := strings.TrimSpace(scanner.Text())
		if text == "" || strings.HasPrefix(text, "#") {
			continue
		}

		key, value, ok := strings.Cut(text, "=")
		if !ok {
			return projectFile{}, fmt.Errorf("line %d: expected `key = value`", line)
		}

		key, value = strings.TrimSpace(key), strings.TrimSpace(value)
		switch key {
		case "project":
			file.project = strings.ToUpper(value)
		case "repo":
			file.repo = value
		default:
			return projectFile{}, fmt.Errorf("line %d: unknown key %q; the file knows `project` and `repo`", line, key)
		}
	}
	if err := scanner.Err(); err != nil {
		return projectFile{}, err
	}

	if file.project == "" {
		return projectFile{}, errors.New("no `project = KEY` line")
	}

	return file, nil
}
