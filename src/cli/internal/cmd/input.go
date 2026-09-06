package cmd

import (
	"fmt"
	"io"
	"os"
	"strings"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
)

// readText reads a Markdown field from a file, or from stdin for `-` — never
// from an editor (VISION 6.1). An empty path means the flag was not given.
func readText(stdin io.Reader, path string) (*string, error) {
	if path == "" {
		return nil, nil
	}

	var data []byte
	var err error
	if path == "-" {
		data, err = io.ReadAll(stdin)
	} else {
		data, err = os.ReadFile(path)
	}
	if err != nil {
		return nil, &config.UsageError{Message: fmt.Sprintf("cannot read %s: %v", path, err)}
	}

	text := strings.TrimRight(string(data), "\n")
	return &text, nil
}

// optional turns the flag package's zero value into "not given".
func optional(s string) *string {
	if s == "" {
		return nil
	}
	return &s
}

// noneOrValue is how a flag clears a field: `none` is JSON null, anything else
// the value, and an empty string is "not given".
func noneOrValue(s string) (given bool, value any) {
	switch s {
	case "":
		return false, nil
	case "none":
		return true, nil
	default:
		return true, s
	}
}
