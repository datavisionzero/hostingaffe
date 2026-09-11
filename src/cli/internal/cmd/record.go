package cmd

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/spf13/cobra"
	"github.com/spf13/pflag"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// The three objects the product is about — machine, software and installation —
// are written the same way, and what they share is here: a body that carries
// only what the command was given, the `--note` that says why, and the
// `--if-match` that guards a write against somebody else's.

// fields collects what a command was actually handed. A flag left off is absent
// and leaves the field alone; a flag given empty is the empty string, which
// clears a text field (docs/api.md, Conventions). Nothing but a map can tell
// those two apart — the generated request objects have one nil for both — which
// is why every write body is built here rather than from them.
type fields struct {
	flags *pflag.FlagSet
	body  map[string]any
}

func given(cmd *cobra.Command) *fields {
	return &fields{flags: cmd.Flags(), body: map[string]any{}}
}

// take records a flag under the name the contract spells it — `private-ip`
// arrives as `private_ip` — and only when the flag was given at all.
func (f *fields) take(flag string, value *string) {
	if f.flags.Changed(flag) {
		f.body[wire(flag)] = *value
	}
}

// set records a value the command did not read off a flag: the key `add` was
// given as an argument, and the anchor two flags were folded into.
func (f *fields) set(field string, value any) {
	f.body[field] = value
}

// takeList replaces a whole list, because a list is never patched entry by
// entry — an entry has no address, and a caller who names two means both
// (docs/api.md, Installations). Repeating the flag adds an entry, and the lone
// value `none` is what clears the list.
func (f *fields) takeList(flag, field string, values []string, entry func(string) (any, error)) error {
	if !f.flags.Changed(flag) {
		return nil
	}

	if len(values) == 1 && values[0] == "none" {
		f.body[field] = []any{}
		return nil
	}

	list := make([]any, 0, len(values))
	for _, value := range values {
		one, err := entry(value)
		if err != nil {
			return &config.UsageError{Message: fmt.Sprintf("--%s %s: %v", flag, value, err)}
		}
		list = append(list, one)
	}

	f.body[field] = list
	return nil
}

// takeMoment records a timestamp the contract wants as RFC 3339. A day is
// accepted as one, because that is how a person writes down when they last
// looked at a machine (VISION 6.1), and it means midnight UTC.
func (f *fields) takeMoment(flag string, value *string) error {
	if !f.flags.Changed(flag) {
		return nil
	}

	for _, layout := range []string{time.RFC3339, "2006-01-02"} {
		if at, err := time.Parse(layout, *value); err == nil {
			f.body[wire(flag)] = at.UTC().Format(time.RFC3339)
			return nil
		}
	}

	return &config.UsageError{
		Message: fmt.Sprintf("--%s %s: a moment is a day, 2026-09-05, or an RFC 3339 timestamp.", flag, *value),
	}
}

// nothing is true when a command was given no field to change, which is a usage
// mistake and never a request.
func (f *fields) nothing() bool { return len(f.body) == 0 }

func (f *fields) json() []byte {
	body, _ := json.Marshal(f.body)
	return body
}

// bytesOf is a body the generated client reads rather than takes.
func bytesOf(body []byte) io.Reader { return bytes.NewReader(body) }

// wire is the name the contract spells a flag with: `snake_case`, always.
func wire(flag string) string { return strings.ReplaceAll(flag, "-", "_") }

// aPort reads the spelling a person writes and a history row keeps —
// `443/tcp:public` — into the object the field actually is (CONTEXT.md,
// Installation). Whether `tcp` and `public` are values of their closed sets is
// the instance's to say: the client does not keep a second copy of the model.
func aPort(text string) (any, error) {
	number, rest, hasProtocol := strings.Cut(text, "/")
	protocol, scope, hasScope := strings.Cut(rest, ":")

	if !hasProtocol || !hasScope {
		return nil, fmt.Errorf("a port reads as 443/tcp:public")
	}

	port, err := strconv.Atoi(number)
	if err != nil {
		return nil, fmt.Errorf("%s is not a port number", number)
	}

	return map[string]any{"port": port, "protocol": protocol, "scope": scope}, nil
}

// aSecret reads the spelling a person writes and a history row keeps —
// `POSTGRES_PASSWORD@/opt/compose/logaffe/.env.runtime`, and the bare name
// where nobody has decided yet where the value goes. The name never carries an
// `@`, so the first one is the one that separates the two.
func aSecret(text string) (any, error) {
	name, path, hasPath := strings.Cut(text, "@")
	if name == "" {
		return nil, fmt.Errorf("a secret is named: NAME, or NAME@/the/file/it/lies/in")
	}
	if !hasPath {
		return map[string]any{"name": name}, nil
	}
	return map[string]any{"name": name, "path": path}, nil
}

// asItIs is the entry of a list that is already what it says: a URL.
func asItIs(text string) (any, error) { return text, nil }

// guard puts the `updated_at` last read on a write, quoted, so that the
// instance refuses a write over one somebody else has moved rather than letting
// it win silently (docs/api.md, Guarding a write).
func guard(ifMatch string) api.RequestEditorFn {
	return func(_ context.Context, req *http.Request) error {
		if ifMatch != "" {
			req.Header.Set("If-Match", `"`+strings.Trim(ifMatch, `"`)+`"`)
		}
		return nil
	}
}

// noteFlag is on every command that writes: the note lands in the history next
// to the change, so that "why" is recorded where "what" is (ADR 0004).
func noteFlag(cmd *cobra.Command, note *string) {
	cmd.Flags().StringVar(note, "note", "", "why, recorded in the history beside the change")
}

// ifMatchFlag is on every guarded write, spelled the same everywhere.
func ifMatchFlag(cmd *cobra.Command, ifMatch *string) {
	cmd.Flags().StringVar(ifMatch, "if-match", "", "the updated_at as last read; refused as stale when it moved")
}

// deleted is what a soft delete prints: it answers 204 and there is no object
// to show, so ha says what happened and how to undo it (planaffe ADR 0013).
func deleted(g *globals, cmd *cobra.Command, object, key string) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), map[string]any{"key": key, "deleted": true})
	}
	fmt.Fprintf(cmd.OutOrStdout(), "%s deleted; `ha %s restore %s` brings it back.\n", key, object, key)
	return nil
}
