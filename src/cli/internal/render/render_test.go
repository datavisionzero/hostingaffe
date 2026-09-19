package render

import (
	"bytes"
	"strings"
	"testing"
	"time"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
)

var at = time.Date(2026, 9, 19, 8, 0, 0, 0, time.UTC)

var actor = api.IdentityRef{Name: "maintainer"}

func say(value string) *string { return &value }

// A birth whose new value is the address of the thing that was born prints
// `created` and nothing else: the path would otherwise stand twice on one line,
// and there is nothing to read in the repetition.
func TestABirthDoesNotPrintTheSubjectBack(t *testing.T) {
	var out bytes.Buffer
	History(&out, []api.HistoryEntry{
		{Actor: actor, At: at, Field: "created", NewValue: say("compose.override.yml")},
	}, "compose.override.yml")

	line := out.String()
	if strings.Contains(line, "→") || strings.Count(line, "compose.override.yml") != 0 {
		t.Errorf("the path stands twice: %q", line)
	}
	if !strings.Contains(line, "created") {
		t.Errorf("the act is still said: %q", line)
	}
}

// And a value that happens to be a birth's but is not the subject's address
// stays: a deployment's first version is what `created` says there.
func TestABirthWithAValueOfItsOwnKeepsIt(t *testing.T) {
	var out bytes.Buffer
	History(&out, []api.HistoryEntry{
		{Actor: actor, At: at, Field: "created", NewValue: say("1.4.0")},
	}, "logaffe-prod")

	if !strings.Contains(out.String(), "→ 1.4.0") {
		t.Errorf("the version: %q", out.String())
	}
}

// A moment is written the way the line's own timestamp is written. A raw
// `2026-09-19T08:00:00.000000Z` beside a column of dates is the one value on the
// screen nobody spelled.
func TestAMomentIsWrittenAsADate(t *testing.T) {
	var out bytes.Buffer
	History(&out, []api.HistoryEntry{
		{Actor: actor, At: at, Field: "measured_at", NewValue: say("2026-09-19T08:00:00.000000Z")},
	}, "ex44")

	if !strings.Contains(out.String(), "→ 2026-09-19 08:00") {
		t.Errorf("the moment: %q", out.String())
	}
	if strings.Contains(out.String(), "000000Z") {
		t.Errorf("the raw form is still there: %q", out.String())
	}
}

// Nothing is guessed: a value that is not an RFC 3339 moment is printed as it
// stands, however much it looks like a date.
func TestWhatIsNotAMomentIsLeftAlone(t *testing.T) {
	for _, value := range []string{"Debian 13", "2026-09-19", "2026-13-45T08:00:00Z", "1.4.0", "v2026-09-19T08:00:00Z"} {
		var out bytes.Buffer
		History(&out, []api.HistoryEntry{
			{Actor: actor, At: at, Field: "os", OldValue: say("Debian 12"), NewValue: say(value)},
		}, "ex44")

		if !strings.Contains(out.String(), "→ "+value) {
			t.Errorf("%q was rewritten: %q", value, out.String())
		}
	}
}

// The reading keeps the same two rules, because the section of a detail screen
// and the reading across everything are two views of the same line.
func TestTheReadingKeepsTheSameRules(t *testing.T) {
	var out bytes.Buffer
	HistoryEvents(&out, []api.HistoryEvent{
		{
			Actor: actor, At: at, SubjectKind: "file", Subject: say("compose.override.yml"),
			Machine: say("ex44"),
			Changes: []api.FieldChange{{Field: "created", NewValue: say("compose.override.yml")}},
		},
		{
			Actor: actor, At: at, SubjectKind: "machine", Subject: say("ex44"), Machine: say("ex44"),
			Changes: []api.FieldChange{
				{Field: "os", OldValue: say("Debian 12"), NewValue: say("Debian 13")},
				{Field: "measured_at", NewValue: say("2026-09-19T08:00:00.000000Z")},
			},
		},
	})

	lines := strings.Split(strings.TrimSuffix(out.String(), "\n"), "\n")
	if len(lines) != 2 {
		t.Fatalf("one line per act:\n%s", out.String())
	}

	// The subject column still names the file; only the change stops repeating it.
	if !strings.Contains(lines[0], "compose.override.yml") || strings.Contains(lines[0], "→") {
		t.Errorf("the birth: %q", lines[0])
	}
	if !strings.HasSuffix(lines[0], "created") {
		t.Errorf("the act is said and the line ends there: %q", lines[0])
	}
	if !strings.Contains(lines[1], "os Debian 12 → Debian 13, measured_at - → 2026-09-19 08:00") {
		t.Errorf("the act: %q", lines[1])
	}
}
