// Package manifest is the one piece of state hostingaffe keeps outside the
// instance: `.ha-sync.json`, lying beside the files `ha files sync` wrote
// (VISION 6.1).
//
// It exists because a command that removes files needs to tell a file it put
// down from one that was always there, and nothing on a host says which is
// which. Two commands read it now — `files sync`, which writes it, and `report
// collect`, which compares what it claims against what lies there (ADR 0017) —
// so the shape lives here rather than in either of them.
package manifest

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// Name is what the manifest is called, beside the files it is about.
const Name = ".ha-sync.json"

// Installation is how an installation owner is written down. An owner is named
// by its kind and its key together, because the machine `caddy` and the
// installation `caddy` are different things (CONTEXT.md, File).
const Installation = "installation"

// Manifest is what sync wrote and what it wrote there.
type Manifest struct {
	Owner string          `json:"owner"`
	Files map[string]File `json:"files"`
}

// File is one path sync wrote, with the revision it wrote and the digest of the
// bytes it put down.
type File struct {
	Revision int32  `json:"revision"`
	Sha256   string `json:"sha256"`
}

// Read reads the manifest beside the files in dir. **A missing one is not an
// error**: it means sync has never written there, and an empty manifest is the
// honest answer. An unreadable one is, because sync would otherwise take every
// file it once wrote for somebody else's and never clear anything away again.
func Read(dir string) (Manifest, error) {
	held := Manifest{Files: map[string]File{}}

	document, err := os.ReadFile(filepath.Join(dir, Name))
	if os.IsNotExist(err) {
		return held, nil
	}
	if err != nil {
		return held, fmt.Errorf("cannot read %s: %v", Name, err)
	}

	if err := json.Unmarshal(document, &held); err != nil {
		return held, fmt.Errorf(
			"%s is not a manifest sync wrote: %v. Remove it to start again, knowing that sync will then take every file there for somebody else's.",
			filepath.Join(dir, Name), err)
	}
	if held.Files == nil {
		held.Files = map[string]File{}
	}
	return held, nil
}

// InstallationKey is the key of the installation the directory holds the files
// of, and whether the owner is an installation at all. A machine never is: sync
// takes none (ADR 0008), so a manifest naming one was not written by any `ha`
// that ever shipped.
func (m Manifest) InstallationKey() (string, bool) {
	key, found := strings.CutPrefix(strings.TrimSpace(m.Owner), Installation+" ")
	key = strings.TrimSpace(key)
	return key, found && key != ""
}
