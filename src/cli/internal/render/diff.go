// Diffing is here rather than behind a dependency because what `ha files diff`
// compares is two revisions of one configuration file — a few hundred lines at
// most — and a unified diff of two such texts is a hundred lines of Go.
package render

import (
	"fmt"
	"io"
	"strings"
)

// cells is how much of a table the longest common subsequence may fill before
// the comparison stops being worth its memory. Two thousand changed lines on
// each side is far past any configuration file; beyond it, the middle is
// reported as one block replaced by another, which is true and cheap.
const cells = 4_000_000

// Diff writes a unified diff of two revisions of one file: `-` for what the
// older one said, `+` for what the newer one says, and three lines of context
// around each change. Nothing at all where the two say the same thing, so that
// a caller can branch on whether anything was printed.
func Diff(w io.Writer, path string, from, to int, older, newer string) {
	fmt.Fprintf(w, "--- %s@%d\n+++ %s@%d\n", path, from, path, to)

	for _, hunk := range hunks(lines(older), lines(newer)) {
		fmt.Fprintf(w, "@@ -%d,%d +%d,%d @@\n", hunk.oldStart, hunk.oldCount, hunk.newStart, hunk.newCount)
		for _, line := range hunk.lines {
			fmt.Fprintln(w, line)
		}
	}
}

// lines splits a stored text the way the record keeps it: no trailing empty
// line, because the content is normalized without one.
func lines(text string) []string {
	if text == "" {
		return nil
	}
	return strings.Split(strings.TrimSuffix(text, "\n"), "\n")
}

type hunk struct {
	oldStart, oldCount, newStart, newCount int
	lines                                  []string
}

// edit is one line of the comparison: kept, removed or added, in order.
type edit struct {
	mark byte
	text string
}

func hunks(older, newer []string) []hunk {
	script := compare(older, newer)

	const context = 3
	var out []hunk
	var current *hunk
	var quiet int
	oldLine, newLine := 1, 1

	// A run of kept lines longer than twice the context closes the hunk it
	// follows and opens no new one until something changes again.
	for index, e := range script {
		if e.mark == ' ' {
			quiet++
			if current != nil && quiet > context {
				out = append(out, *current)
				current = nil
			}
		} else {
			if current == nil {
				start := max(0, index-context)
				current = &hunk{oldStart: oldLine, newStart: newLine}
				for _, before := range script[start:index] {
					current.lines = append(current.lines, " "+before.text)
					current.oldStart--
					current.newStart--
					current.oldCount++
					current.newCount++
				}
			}
			quiet = 0
		}

		if current != nil {
			current.lines = append(current.lines, string(e.mark)+e.text)
			if e.mark != '+' {
				current.oldCount++
			}
			if e.mark != '-' {
				current.newCount++
			}
		}

		if e.mark != '+' {
			oldLine++
		}
		if e.mark != '-' {
			newLine++
		}
	}

	if current != nil {
		out = append(out, *current)
	}
	return out
}

// compare is the edit script: the common prefix and suffix taken off first,
// because two revisions of a configuration file are mostly the same file, and
// the longest common subsequence over what is left.
func compare(older, newer []string) []edit {
	prefix := 0
	for prefix < len(older) && prefix < len(newer) && older[prefix] == newer[prefix] {
		prefix++
	}

	suffix := 0
	for suffix < len(older)-prefix && suffix < len(newer)-prefix &&
		older[len(older)-1-suffix] == newer[len(newer)-1-suffix] {
		suffix++
	}

	script := make([]edit, 0, len(older)+len(newer))
	for _, line := range older[:prefix] {
		script = append(script, edit{' ', line})
	}

	script = append(script, middle(older[prefix:len(older)-suffix], newer[prefix:len(newer)-suffix])...)

	for _, line := range older[len(older)-suffix:] {
		script = append(script, edit{' ', line})
	}
	return script
}

func middle(older, newer []string) []edit {
	// Past the ceiling the table would cost more than the answer is worth, and
	// the honest cheap answer is that this block became that one.
	if len(older)*len(newer) > cells {
		script := make([]edit, 0, len(older)+len(newer))
		for _, line := range older {
			script = append(script, edit{'-', line})
		}
		for _, line := range newer {
			script = append(script, edit{'+', line})
		}
		return script
	}

	// The length of the longest common subsequence of every pair of suffixes,
	// which the walk below reads backwards into the script.
	common := make([][]int, len(older)+1)
	for i := range common {
		common[i] = make([]int, len(newer)+1)
	}
	for i := len(older) - 1; i >= 0; i-- {
		for j := len(newer) - 1; j >= 0; j-- {
			if older[i] == newer[j] {
				common[i][j] = common[i+1][j+1] + 1
			} else {
				common[i][j] = max(common[i+1][j], common[i][j+1])
			}
		}
	}

	var script []edit
	i, j := 0, 0
	for i < len(older) && j < len(newer) {
		switch {
		case older[i] == newer[j]:
			script = append(script, edit{' ', older[i]})
			i++
			j++
		case common[i+1][j] >= common[i][j+1]:
			script = append(script, edit{'-', older[i]})
			i++
		default:
			script = append(script, edit{'+', newer[j]})
			j++
		}
	}
	for ; i < len(older); i++ {
		script = append(script, edit{'-', older[i]})
	}
	for ; j < len(newer); j++ {
		script = append(script, edit{'+', newer[j]})
	}
	return script
}
