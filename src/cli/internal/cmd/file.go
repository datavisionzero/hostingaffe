package cmd

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"strconv"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// Files are the one record whose history keeps content: every write is a
// revision, and every revision is still readable. A file has no key — its
// address is its owner and its path — so every verb here names both.
func newFile(g *globals) *cobra.Command {
	cmd := &cobra.Command{
		Use:     "files",
		Short:   "Files: the text a machine runs with, under the machine or the installation it belongs to.",
		Aliases: []string{"file"},
	}
	cmd.AddCommand(
		newFileList(g), newFileGet(g), newFilePut(g), newFileDiff(g),
		newFileRevisions(g), newFileDelete(g), newFileRestore(g), newFileHistory(g),
		newFileSync(g))
	return cmd
}

// anchor is the owner a file hangs under, named the way a page names what it
// hangs on: the kind belongs to it, because the machine `caddy` and the
// software `caddy` are different things.
type anchor struct {
	machine, installation string
	kind                  api.AnchorKind
	key                   string
}

func (a *anchor) flags(cmd *cobra.Command) {
	cmd.Flags().StringVar(&a.machine, "machine", "", "the machine the file belongs to, by `key`")
	cmd.Flags().StringVar(&a.installation, "installation", "", "the installation the file belongs to, by `key`")
}

// resolve settles the owner before any request goes out: naming both or neither
// is a mistake in the arguments, and a file has exactly one owner.
func (a *anchor) resolve() error {
	switch {
	case a.machine != "" && a.installation != "":
		return &config.UsageError{Message: "a file belongs to one thing: --machine or --installation, not both."}
	case a.machine != "":
		a.kind, a.key = api.AnchorKindMachine, a.machine
	case a.installation != "":
		a.kind, a.key = api.AnchorKindInstallation, a.installation
	default:
		return &config.UsageError{Message: "a file belongs to a machine or an installation: --machine KEY or --installation KEY."}
	}
	return nil
}

// String is how an owner is written down: the kind and the key, because the
// machine `caddy` and the installation `caddy` are different things and a
// manifest that said only the key would not know which it holds.
func (a anchor) String() string { return string(a.kind) + " " + a.key }

func printFile(g *globals, cmd *cobra.Command, file api.File) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), file)
	}
	render.File(cmd.OutOrStdout(), file)
	return nil
}

// answer is what every call below hands back: enough to check the response and
// nothing that depends on which owner it went to.
type answer[T any] struct {
	response *http.Response
	body     []byte
	value    *T
}

func (a answer[T]) checked() (*T, error) {
	if err := client.Check(a.response, a.body); err != nil {
		return nil, err
	}
	return a.value, nil
}

func newFileList(g *globals) *cobra.Command {
	var owner anchor
	cmd := &cobra.Command{
		Use: "list --machine KEY | --installation KEY", Short: "Every file of the owner, by path, without the contents.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			if err := owner.resolve(); err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			got, err := listFiles(cmd.Context(), c, owner)
			if err != nil {
				return err
			}
			files, err := got.checked()
			if err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), files)
			}
			render.FileSummaries(cmd.OutOrStdout(), *files)
			return nil
		},
	}
	owner.flags(cmd)
	return cmd
}

// newFileGet prints the content and nothing else, so that it can be redirected
// into a file. `--json` gives the record instead, with the revision a write
// hands back.
func newFileGet(g *globals) *cobra.Command {
	var owner anchor
	var revision int32
	cmd := &cobra.Command{
		Use: "get PATH --machine KEY | --installation KEY", Short: "The content, as it is stored. --revision reads it as it was at that write.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if err := owner.resolve(); err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}

			var asked *int32
			if cmd.Flags().Changed("revision") {
				asked = &revision
			}
			got, err := readFile(cmd.Context(), c, owner, args[0], asked)
			if err != nil {
				return err
			}
			file, err := got.checked()
			if err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), file)
			}
			// Byte for byte, so that `ha files get x > x` writes the file
			// the machine runs and not one line more.
			fmt.Fprint(cmd.OutOrStdout(), file.Content)
			return nil
		},
	}
	owner.flags(cmd)
	cmd.Flags().Int32Var(&revision, "revision", 0, "read the file as it was at this write; without it, as it is now")
	return cmd
}

// newFilePut writes, and creates where there is nothing yet. It writes first
// and creates on a not-found, because writing is the ordinary case and the
// first write of a file happens once.
func newFilePut(g *globals) *cobra.Command {
	var owner anchor
	var file, note string
	var revision int32
	var executable bool
	cmd := &cobra.Command{
		Use: "put PATH --file FILE --machine KEY | --installation KEY", Short: "Write the file: a new revision, unless it already says exactly this.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if err := owner.resolve(); err != nil {
				return err
			}
			if file == "" {
				return &config.UsageError{Message: "the content comes from a file or from stdin: --file PATH or --file -."}
			}
			content, err := readContent(cmd.InOrStdin(), file)
			if err != nil {
				return err
			}

			_, c, err := g.load()
			if err != nil {
				return err
			}

			body := map[string]any{"content": content}
			if cmd.Flags().Changed("executable") {
				body["executable"] = executable
			}

			guarded := ""
			if cmd.Flags().Changed("revision") {
				guarded = strconv.Itoa(int(revision))
			}

			written, err := writeFile(cmd.Context(), c, owner, args[0], body, guarded, note)
			if err != nil {
				return err
			}

			// Nothing at that path yet, and no revision to write against: this
			// is the file's first write, which is a create.
			if isNotFound(written.response) && guarded == "" {
				body["path"] = args[0]
				created, err := createFile(cmd.Context(), c, owner, body, note)
				if err != nil {
					return err
				}
				made, err := created.checked()
				if err != nil {
					return err
				}
				return printFile(g, cmd, *made)
			}

			result, err := written.checked()
			if err != nil {
				return err
			}
			return printFile(g, cmd, *result)
		},
	}
	owner.flags(cmd)
	cmd.Flags().StringVar(&file, "file", "", "the content, from a file or `-` for stdin")
	cmd.Flags().Int32Var(&revision, "revision", 0, "the revision as last read; refused as stale when somebody wrote since")
	cmd.Flags().BoolVar(&executable, "executable", false, "the one mode bit there is")
	noteFlag(cmd, &note)
	return cmd
}

// isNotFound is asked before the response is turned into a failure, because a
// missing file is what `put` answers by creating one rather than by stopping.
func isNotFound(response *http.Response) bool {
	return response != nil && response.StatusCode == http.StatusNotFound
}

// newFileDiff puts two revisions side by side. Without either end it shows the
// last change, which is the question somebody usually has.
func newFileDiff(g *globals) *cobra.Command {
	var owner anchor
	var from, to int32
	cmd := &cobra.Command{
		Use: "diff PATH --machine KEY | --installation KEY", Short: "What changed between two revisions; without --from and --to, the last change.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if err := owner.resolve(); err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}

			newer, older, err := ends(cmd, c, owner, args[0], from, to)
			if err != nil {
				return err
			}
			if newer.Revision <= 1 && older == nil {
				return &config.UsageError{
					Message: fmt.Sprintf("%s has one revision; there is nothing to compare it with.", args[0]),
				}
			}

			was, becomes := "", newer.Content
			at := int32(0)
			if older != nil {
				was, at = older.Content, older.Revision
			}
			render.Diff(cmd.OutOrStdout(), args[0], int(at), int(newer.Revision), was, becomes)
			return nil
		},
	}
	owner.flags(cmd)
	cmd.Flags().Int32Var(&from, "from", 0, "the older revision; without it, the one before --to")
	cmd.Flags().Int32Var(&to, "to", 0, "the newer revision; without it, the one the file is at")
	return cmd
}

// ends reads the two revisions a diff compares: the newer one first, because
// what the older one defaults to is the one before it.
func ends(cmd *cobra.Command, c *client.Client, owner anchor, path string, from, to int32) (*api.File, *api.File, error) {
	var asked *int32
	if cmd.Flags().Changed("to") {
		asked = &to
	}
	got, err := readFile(cmd.Context(), c, owner, path, asked)
	if err != nil {
		return nil, nil, err
	}
	newer, err := got.checked()
	if err != nil {
		return nil, nil, err
	}

	earlier := newer.Revision - 1
	if cmd.Flags().Changed("from") {
		earlier = from
	}
	if earlier < 1 {
		return newer, nil, nil
	}

	got, err = readFile(cmd.Context(), c, owner, path, &earlier)
	if err != nil {
		return nil, nil, err
	}
	older, err := got.checked()
	return newer, older, err
}

func newFileRevisions(g *globals) *cobra.Command {
	var owner anchor
	cmd := &cobra.Command{
		Use: "revisions PATH --machine KEY | --installation KEY", Short: "Every write of the file, newest first, without what each of them wrote.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if err := owner.resolve(); err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			got, err := fileRevisions(cmd.Context(), c, owner, args[0])
			if err != nil {
				return err
			}
			revisions, err := got.checked()
			if err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), revisions)
			}
			render.FileRevisions(cmd.OutOrStdout(), *revisions)
			return nil
		},
	}
	owner.flags(cmd)
	return cmd
}

func newFileDelete(g *globals) *cobra.Command {
	var owner anchor
	var note string
	cmd := &cobra.Command{
		Use: "delete PATH --machine KEY | --installation KEY", Short: "Soft-delete the file with every revision it ever had; its path stays spent until the purge.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if err := owner.resolve(); err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			got, err := deleteFile(cmd.Context(), c, owner, args[0], note)
			if err != nil {
				return err
			}
			if err := client.Check(got.response, got.body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), map[string]any{
					"owner": map[string]any{"kind": owner.kind, "key": owner.key},
					"path":  args[0], "deleted": true})
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s deleted; `ha files restore %s --%s %s` brings it back.\n",
				args[0], args[0], owner.kind, owner.key)
			return nil
		},
	}
	owner.flags(cmd)
	noteFlag(cmd, &note)
	return cmd
}

func newFileRestore(g *globals) *cobra.Command {
	var owner anchor
	var note string
	cmd := &cobra.Command{
		Use: "restore PATH --machine KEY | --installation KEY", Short: "Bring a deleted file back, with its revisions, at the path it kept.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if err := owner.resolve(); err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			got, err := restoreFile(cmd.Context(), c, owner, args[0], note)
			if err != nil {
				return err
			}
			file, err := got.checked()
			if err != nil {
				return err
			}
			return printFile(g, cmd, *file)
		},
	}
	owner.flags(cmd)
	noteFlag(cmd, &note)
	return cmd
}

func newFileHistory(g *globals) *cobra.Command {
	var owner anchor
	cmd := &cobra.Command{
		Use: "history PATH --machine KEY | --installation KEY", Short: "Every change to the file, oldest first: who, when, and which revision it became.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if err := owner.resolve(); err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			got, err := fileHistory(cmd.Context(), c, owner, args[0])
			if err != nil {
				return err
			}
			entries, err := got.checked()
			if err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), entries)
			}
			render.History(cmd.OutOrStdout(), *entries)
			return nil
		},
	}
	owner.flags(cmd)
	return cmd
}

// Both owners get the same endpoints under a different prefix, and the
// generated client has a method per pair. The eight functions below are where
// that pair becomes one call again, so that no verb above has to know there are
// two of them.

func listFiles(ctx context.Context, c *client.Client, o anchor) (answer[[]api.FileSummary], error) {
	if o.kind == api.AnchorKindMachine {
		r, err := c.ListMachineFilesWithResponse(ctx, o.key)
		if err != nil {
			return answer[[]api.FileSummary]{}, client.Transport(err)
		}
		return answer[[]api.FileSummary]{r.HTTPResponse, r.Body, r.JSON200}, nil
	}
	r, err := c.ListInstallationFilesWithResponse(ctx, o.key)
	if err != nil {
		return answer[[]api.FileSummary]{}, client.Transport(err)
	}
	return answer[[]api.FileSummary]{r.HTTPResponse, r.Body, r.JSON200}, nil
}

func readFile(ctx context.Context, c *client.Client, o anchor, path string, revision *int32) (answer[api.File], error) {
	if o.kind == api.AnchorKindMachine {
		r, err := c.ReadMachineFileWithResponse(ctx, o.key, path, &api.ReadMachineFileParams{Revision: revision})
		if err != nil {
			return answer[api.File]{}, client.Transport(err)
		}
		return answer[api.File]{r.HTTPResponse, r.Body, r.JSON200}, nil
	}
	r, err := c.ReadInstallationFileWithResponse(ctx, o.key, path, &api.ReadInstallationFileParams{Revision: revision})
	if err != nil {
		return answer[api.File]{}, client.Transport(err)
	}
	return answer[api.File]{r.HTTPResponse, r.Body, r.JSON200}, nil
}

func writeFile(ctx context.Context, c *client.Client, o anchor, path string, body map[string]any, revision, note string) (answer[api.File], error) {
	content, _ := json.Marshal(body)
	if o.kind == api.AnchorKindMachine {
		r, err := c.WriteMachineFileWithBodyWithResponse(
			ctx, o.key, path, &api.WriteMachineFileParams{Note: optional(note)},
			"application/json", bytesOf(content), guard(revision))
		if err != nil {
			return answer[api.File]{}, client.Transport(err)
		}
		return answer[api.File]{r.HTTPResponse, r.Body, r.JSON200}, nil
	}
	r, err := c.WriteInstallationFileWithBodyWithResponse(
		ctx, o.key, path, &api.WriteInstallationFileParams{Note: optional(note)},
		"application/json", bytesOf(content), guard(revision))
	if err != nil {
		return answer[api.File]{}, client.Transport(err)
	}
	return answer[api.File]{r.HTTPResponse, r.Body, r.JSON200}, nil
}

func createFile(ctx context.Context, c *client.Client, o anchor, body map[string]any, note string) (answer[api.File], error) {
	content, _ := json.Marshal(body)
	if o.kind == api.AnchorKindMachine {
		r, err := c.CreateMachineFileWithBodyWithResponse(
			ctx, o.key, &api.CreateMachineFileParams{Note: optional(note)}, "application/json", bytesOf(content))
		if err != nil {
			return answer[api.File]{}, client.Transport(err)
		}
		return answer[api.File]{r.HTTPResponse, r.Body, r.JSON201}, nil
	}
	r, err := c.CreateInstallationFileWithBodyWithResponse(
		ctx, o.key, &api.CreateInstallationFileParams{Note: optional(note)}, "application/json", bytesOf(content))
	if err != nil {
		return answer[api.File]{}, client.Transport(err)
	}
	return answer[api.File]{r.HTTPResponse, r.Body, r.JSON201}, nil
}

func deleteFile(ctx context.Context, c *client.Client, o anchor, path, note string) (answer[struct{}], error) {
	if o.kind == api.AnchorKindMachine {
		r, err := c.DeleteMachineFileWithResponse(ctx, o.key, path, &api.DeleteMachineFileParams{Note: optional(note)})
		if err != nil {
			return answer[struct{}]{}, client.Transport(err)
		}
		return answer[struct{}]{response: r.HTTPResponse, body: r.Body}, nil
	}
	r, err := c.DeleteInstallationFileWithResponse(ctx, o.key, path, &api.DeleteInstallationFileParams{Note: optional(note)})
	if err != nil {
		return answer[struct{}]{}, client.Transport(err)
	}
	return answer[struct{}]{response: r.HTTPResponse, body: r.Body}, nil
}

func restoreFile(ctx context.Context, c *client.Client, o anchor, path, note string) (answer[api.File], error) {
	if o.kind == api.AnchorKindMachine {
		r, err := c.RestoreMachineFileWithResponse(ctx, o.key, path, &api.RestoreMachineFileParams{Note: optional(note)})
		if err != nil {
			return answer[api.File]{}, client.Transport(err)
		}
		return answer[api.File]{r.HTTPResponse, r.Body, r.JSON200}, nil
	}
	r, err := c.RestoreInstallationFileWithResponse(ctx, o.key, path, &api.RestoreInstallationFileParams{Note: optional(note)})
	if err != nil {
		return answer[api.File]{}, client.Transport(err)
	}
	return answer[api.File]{r.HTTPResponse, r.Body, r.JSON200}, nil
}

func fileRevisions(ctx context.Context, c *client.Client, o anchor, path string) (answer[[]api.FileRevision], error) {
	if o.kind == api.AnchorKindMachine {
		r, err := c.ReadMachineFileRevisionsWithResponse(ctx, o.key, path)
		if err != nil {
			return answer[[]api.FileRevision]{}, client.Transport(err)
		}
		return answer[[]api.FileRevision]{r.HTTPResponse, r.Body, r.JSON200}, nil
	}
	r, err := c.ReadInstallationFileRevisionsWithResponse(ctx, o.key, path)
	if err != nil {
		return answer[[]api.FileRevision]{}, client.Transport(err)
	}
	return answer[[]api.FileRevision]{r.HTTPResponse, r.Body, r.JSON200}, nil
}

func fileHistory(ctx context.Context, c *client.Client, o anchor, path string) (answer[[]api.HistoryEntry], error) {
	if o.kind == api.AnchorKindMachine {
		r, err := c.ReadMachineFileHistoryWithResponse(ctx, o.key, path)
		if err != nil {
			return answer[[]api.HistoryEntry]{}, client.Transport(err)
		}
		return answer[[]api.HistoryEntry]{r.HTTPResponse, r.Body, r.JSON200}, nil
	}
	r, err := c.ReadInstallationFileHistoryWithResponse(ctx, o.key, path)
	if err != nil {
		return answer[[]api.HistoryEntry]{}, client.Transport(err)
	}
	return answer[[]api.HistoryEntry]{r.HTTPResponse, r.Body, r.JSON200}, nil
}
