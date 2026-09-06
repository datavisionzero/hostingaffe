package cmd

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// Manifest is the name of the one piece of state outside Postgres, kept beside
// the files it is about (VISION 6.1).
const Manifest = ".ha-sync.json"

// `ha files sync` is the only command that touches a machine, and it pulls: it
// runs on the host under the token of the SSH session, writes the owner's
// current files into place, and stops. It executes nothing — no `docker compose
// up`, no reload, no check that anything came up (VISION 5, 13). What to do
// after it is in the runbook, and the agent does it.
func newFileSync(g *globals) *cobra.Command {
	var owner anchor
	var dry bool
	cmd := &cobra.Command{
		Use:   "sync DIR --machine KEY | --installation KEY",
		Short: "Write the owner's current files into a directory on this host. It executes nothing.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if err := owner.resolve(); err != nil {
				return err
			}

			// The directory is settled before a single request goes out: one
			// that holds another owner's files is a mistake to say, not one to
			// find out about after reading a record.
			held, err := heldBy(args[0])
			if err != nil {
				return err
			}
			if held.Owner != "" && held.Owner != owner.String() {
				return &config.UsageError{Message: fmt.Sprintf(
					"%s holds the files of %s; syncing %s into it would mix two owners' files.",
					args[0], held.Owner, owner)}
			}

			_, c, err := g.load()
			if err != nil {
				return err
			}
			wanted, err := filesOf(cmd.Context(), c, owner)
			if err != nil {
				return err
			}

			decided, err := planned(args[0], owner, held, wanted)
			if err != nil {
				return err
			}

			if !dry {
				if err := apply(args[0], decided); err != nil {
					return err
				}
			}

			return sayPlan(g, cmd, decided, dry)
		},
	}
	owner.flags(cmd)
	cmd.Flags().BoolVar(&dry, "dry-run", false, "say what it would do and touch nothing")
	return cmd
}

// What sync decided about one path. The words are the ones it prints, and they
// are what the rule of the manifest reads as: what sync wrote, sync clears away;
// what it never wrote, it never touches.
const (
	written   = "written"    // it was not there, and now it is
	changed   = "changed"    // sync wrote it before, and the record has moved
	unchanged = "unchanged"  // it already says exactly this
	restored  = "restored"   // sync wrote it, the host changed it, the record wins
	removed   = "removed"    // sync wrote it, and it has left the record
	kept      = "kept"       // sync wrote it, the host changed it, and it has left the record
	inTheWay  = "in the way" // the record has it and sync never wrote what is there
)

type step struct {
	Path       string `json:"path"`
	Does       string `json:"does"`
	Why        string `json:"why,omitempty"`
	Content    string `json:"-"`
	Executable bool   `json:"-"`
	Revision   int32  `json:"revision,omitempty"`
}

type plan struct {
	Steps    []step
	Manifest manifest
}

// manifest is what sync wrote and what it wrote there: the only way to tell a
// file it put down from one that was always there. Without it, a command that
// removes files would be unusable.
type manifest struct {
	Owner string                `json:"owner"`
	Files map[string]manifested `json:"files"`
}

type manifested struct {
	Revision int32  `json:"revision"`
	Sha256   string `json:"sha256"`
}

// planned decides everything before anything is written, so that `--dry-run`
// prints exactly what a run would do.
func planned(dir string, owner anchor, held manifest, wanted []api.File) (*plan, error) {
	made := manifest{Owner: owner.String(), Files: map[string]manifested{}}
	steps := []step{}
	inRecord := map[string]bool{}

	for _, file := range wanted {
		if !safePath(file.Path) {
			return nil, &config.UsageError{Message: fmt.Sprintf(
				"the instance answered with the path %q, which does not stay inside %s.", file.Path, dir)}
		}
		if file.Path == Manifest {
			return nil, &config.UsageError{Message: fmt.Sprintf(
				"the record has a file at %s, which is where sync keeps its own manifest.", Manifest)}
		}
		inRecord[file.Path] = true

		digest := sha256Of(file.Content)
		made.Files[file.Path] = manifested{Revision: file.Revision, Sha256: digest}

		onDisk, mode, there := onDiskAt(dir, file.Path)
		was, mine := held.Files[file.Path]

		switch {
		case !there && !mine:
			steps = append(steps, putting(file, written, ""))
		case !there:
			steps = append(steps, putting(file, written, "sync wrote it before and it is gone"))
		case !mine:
			// Something sync never wrote is at that path. It is not sync's to
			// replace, and it stays exactly as it is.
			delete(made.Files, file.Path)
			steps = append(steps, step{Path: file.Path, Does: inTheWay, Why: "sync never wrote it"})
		case sha256Of(onDisk) == digest && mode == modeOf(file.Executable):
			steps = append(steps, step{Path: file.Path, Does: unchanged, Revision: file.Revision})
		case sha256Of(onDisk) != was.Sha256:
			steps = append(steps, putting(file, restored, "it had been changed on the host"))
		default:
			steps = append(steps, putting(file, changed, fmt.Sprintf("revision %d, was %d", file.Revision, was.Revision)))
		}
	}

	// What sync wrote and the record no longer has. It goes, unless the host
	// changed it since — then it stops being sync's and becomes whoever's
	// changed it, and the manifest forgets it.
	for path, was := range held.Files {
		if inRecord[path] {
			continue
		}
		onDisk, _, there := onDiskAt(dir, path)
		switch {
		case !there:
			steps = append(steps, step{Path: path, Does: removed, Why: "it was already gone"})
		case sha256Of(onDisk) != was.Sha256:
			steps = append(steps, step{Path: path, Does: kept, Why: "changed on the host since sync wrote it"})
		default:
			steps = append(steps, step{Path: path, Does: removed})
		}
	}

	sort.SliceStable(steps, func(a, b int) bool { return steps[a].Path < steps[b].Path })
	return &plan{Steps: steps, Manifest: made}, nil
}

func putting(file api.File, does, why string) step {
	return step{
		Path:       file.Path,
		Does:       does,
		Why:        why,
		Content:    file.Content,
		Executable: file.Executable,
		Revision:   file.Revision,
	}
}

// apply does what the plan says and then writes the manifest, so that a run
// interrupted halfway leaves a manifest that claims less than it wrote rather
// than more: sync would then find its own files in the way and say so, which is
// the safe half of being wrong.
func apply(dir string, p *plan) error {
	for _, one := range p.Steps {
		at := filepath.Join(dir, filepath.FromSlash(one.Path))

		switch one.Does {
		case written, changed, restored:
			if err := os.MkdirAll(filepath.Dir(at), 0o755); err != nil {
				return &config.UsageError{Message: fmt.Sprintf("cannot write %s: %v", at, err)}
			}
			if err := os.WriteFile(at, []byte(one.Content), modeOf(one.Executable)); err != nil {
				return &config.UsageError{Message: fmt.Sprintf("cannot write %s: %v", at, err)}
			}
		case unchanged:
			if err := os.Chmod(at, modeOf(one.Executable)); err != nil {
				return &config.UsageError{Message: fmt.Sprintf("cannot set the mode of %s: %v", at, err)}
			}
		case removed:
			if err := os.Remove(at); err != nil && !os.IsNotExist(err) {
				return &config.UsageError{Message: fmt.Sprintf("cannot remove %s: %v", at, err)}
			}
			prune(dir, filepath.Dir(at))
		}
	}

	document, err := json.MarshalIndent(p.Manifest, "", "  ")
	if err != nil {
		return err
	}
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("cannot write into %s: %v", dir, err)}
	}
	if err := os.WriteFile(filepath.Join(dir, Manifest), append(document, '\n'), 0o644); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("cannot write %s: %v", Manifest, err)}
	}
	return nil
}

// prune takes away the directories a removal emptied, up to but never including
// the directory sync was given.
func prune(root, at string) {
	root = filepath.Clean(root)
	for at = filepath.Clean(at); at != root && strings.HasPrefix(at, root+string(filepath.Separator)); at = filepath.Dir(at) {
		if os.Remove(at) != nil {
			return
		}
	}
}

func sayPlan(g *globals, cmd *cobra.Command, p *plan, dry bool) error {
	if g.json {
		if err := render.JSON(cmd.OutOrStdout(), p.Steps); err != nil {
			return err
		}
	} else {
		for _, one := range p.Steps {
			fmt.Fprintf(cmd.OutOrStdout(), "%-10s %s", one.Does, one.Path)
			if one.Why != "" {
				fmt.Fprintf(cmd.OutOrStdout(), "  (%s)", one.Why)
			}
			fmt.Fprintln(cmd.OutOrStdout())
		}
		if dry {
			fmt.Fprintln(cmd.OutOrStdout(), "nothing was written: --dry-run.")
		}
	}

	// A file in the way means the directory is not what the record says, and a
	// script has to be able to tell.
	for _, one := range p.Steps {
		if one.Does == inTheWay {
			return &syncConflict{}
		}
	}
	return nil
}

// syncConflict is the one refusal sync makes itself: the record and the disk
// disagree about a path, and sync will not settle it by overwriting what it
// never wrote.
type syncConflict struct{}

func (c *syncConflict) Error() string {
	return "some files are in the way: what sync never wrote, sync does not replace."
}

func (c *syncConflict) ExitCode() int { return exit.Conflict }

// heldBy reads the manifest, and an unreadable one is a mistake to say out loud
// rather than to work around: sync would otherwise take every file it once
// wrote for somebody else's and never clear anything away again.
func heldBy(dir string) (manifest, error) {
	held := manifest{Files: map[string]manifested{}}

	document, err := os.ReadFile(filepath.Join(dir, Manifest))
	if os.IsNotExist(err) {
		return held, nil
	}
	if err != nil {
		return held, &config.UsageError{Message: fmt.Sprintf("cannot read %s: %v", Manifest, err)}
	}

	if err := json.Unmarshal(document, &held); err != nil {
		return held, &config.UsageError{Message: fmt.Sprintf(
			"%s is not a manifest sync wrote: %v. Remove it to start again, knowing that sync will then take every file there for somebody else's.",
			filepath.Join(dir, Manifest), err)}
	}
	if held.Files == nil {
		held.Files = map[string]manifested{}
	}
	return held, nil
}

func onDiskAt(dir, path string) (content string, mode fs.FileMode, there bool) {
	at := filepath.Join(dir, filepath.FromSlash(path))

	info, err := os.Stat(at)
	if err != nil || info.IsDir() {
		return "", 0, false
	}
	bytes, err := os.ReadFile(at)
	if err != nil {
		return "", 0, false
	}
	return string(bytes), info.Mode().Perm(), true
}

// modeOf is the one mode bit the record has: executable, or not.
func modeOf(executable bool) fs.FileMode {
	if executable {
		return 0o755
	}
	return 0o644
}

func sha256Of(content string) string {
	sum := sha256.Sum256([]byte(content))
	return hex.EncodeToString(sum[:])
}
