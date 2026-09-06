package cmd

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"sort"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// The escape hatch that makes the product safe to adopt (VISION 14): the whole
// record as a Markdown tree with the files at their places, plus one JSON
// document that carries all of it machine-readably, the history included.
//
// It is composed here, out of the ordinary endpoints, the way planaffe's export
// is. Whoever may read an export may make the single reads it is built from, and
// an endpoint for it would be a second place that has to learn every entity the
// model ever grows.
func newExport(g *globals) *cobra.Command {
	var dir string
	cmd := &cobra.Command{
		Use: "export --dir PATH", Short: "The whole record as a Markdown tree with the files in place, plus JSON.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			if dir == "" {
				return &config.UsageError{Message: "an export is written somewhere: --dir PATH."}
			}
			if err := clearForExport(dir); err != nil {
				return err
			}

			_, c, err := g.load()
			if err != nil {
				return err
			}

			record, err := gather(cmd.Context(), c)
			if err != nil {
				return err
			}
			if err := write(dir, record); err != nil {
				return err
			}

			if g.json {
				return render.JSON(cmd.OutOrStdout(), map[string]any{
					"dir":           dir,
					"machines":      len(record.Machines),
					"software":      len(record.Software),
					"pages":         len(record.Pages),
					"installations": record.installations(),
					"files":         record.files(),
				})
			}
			fmt.Fprintf(cmd.OutOrStdout(),
				"%s: %d machines, %d installations, %d software, %d files, %d pages.\n",
				dir, len(record.Machines), record.installations(), len(record.Software), record.files(), len(record.Pages))
			return nil
		},
	}
	cmd.Flags().StringVar(&dir, "dir", "", "where to write the export; it must not exist, be empty, or be an export already")
	return cmd
}

// The shape of the JSON, which is also the shape the bulk write reads back:
// export and import go in a circle (VISION 6.1, 14). Every object is the one
// the API answered with; what nests under it is what belongs to it.
type record struct {
	Machines []machineRecord  `json:"machines"`
	Software []softwareRecord `json:"software"`
	Pages    []pageRecord     `json:"pages"`
}

type machineRecord struct {
	api.Machine
	Installations []installationRecord `json:"installations"`
	Files         []api.File           `json:"files"`
	History       []api.HistoryEntry   `json:"history"`
}

type installationRecord struct {
	api.Installation
	Deployments []api.Deployment   `json:"deployments"`
	Files       []api.File         `json:"files"`
	History     []api.HistoryEntry `json:"history"`
}

type softwareRecord struct {
	api.Software
	History []api.HistoryEntry `json:"history"`
}

type pageRecord struct {
	api.Page
	History []api.HistoryEntry `json:"history"`
}

func (r *record) installations() int {
	count := 0
	for _, machine := range r.Machines {
		count += len(machine.Installations)
	}
	return count
}

func (r *record) files() int {
	count := 0
	for _, machine := range r.Machines {
		count += len(machine.Files)
		for _, installation := range machine.Installations {
			count += len(installation.Files)
		}
	}
	return count
}

// clearForExport settles where the export may be written before a single
// request goes out. An empty directory or none at all is written into; one that
// already holds an export is replaced, because re-exporting is the ordinary
// thing to do; anything else is left alone, because `ha` does not delete what it
// did not write.
func clearForExport(dir string) error {
	entries, err := os.ReadDir(dir)
	switch {
	case os.IsNotExist(err):
		return nil
	case err != nil:
		return &config.UsageError{Message: fmt.Sprintf("cannot write into %s: %v", dir, err)}
	case len(entries) == 0:
		return nil
	}

	if _, err := os.Stat(filepath.Join(dir, "export.json")); err != nil {
		return &config.UsageError{
			Message: fmt.Sprintf("%s is not empty and is not an export; nothing there is ha's to remove.", dir),
		}
	}

	if err := os.RemoveAll(dir); err != nil {
		return &config.UsageError{Message: fmt.Sprintf("cannot replace the export in %s: %v", dir, err)}
	}
	return nil
}

// gather reads the whole record, in the order the tree is written in and by key
// throughout, so that two exports of the same record are the same bytes.
func gather(ctx context.Context, c *client.Client) (*record, error) {
	whole := &record{Machines: []machineRecord{}, Software: []softwareRecord{}, Pages: []pageRecord{}}

	retired := true
	machines, err := c.ListMachinesWithResponse(ctx, &api.ListMachinesParams{Retired: &retired})
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(machines.HTTPResponse, machines.Body); err != nil {
		return nil, err
	}

	installations, err := c.ListInstallationsWithResponse(ctx, &api.ListInstallationsParams{Retired: &retired})
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(installations.HTTPResponse, installations.Body); err != nil {
		return nil, err
	}

	under := map[string][]string{}
	for _, one := range *installations.JSON200 {
		under[one.Machine] = append(under[one.Machine], one.Key)
	}

	for _, summary := range sorted(*machines.JSON200, func(m api.MachineSummary) string { return m.Key }) {
		machine, err := oneMachine(ctx, c, summary.Key, under[summary.Key])
		if err != nil {
			return nil, err
		}
		whole.Machines = append(whole.Machines, *machine)
	}

	programs, err := c.ListSoftwareWithResponse(ctx)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(programs.HTTPResponse, programs.Body); err != nil {
		return nil, err
	}

	for _, summary := range sorted(*programs.JSON200, func(s api.SoftwareSummary) string { return s.Key }) {
		read, err := c.ReadSoftwareWithResponse(ctx, summary.Key)
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(read.HTTPResponse, read.Body); err != nil {
			return nil, err
		}
		history, err := c.ReadSoftwareHistoryWithResponse(ctx, summary.Key)
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(history.HTTPResponse, history.Body); err != nil {
			return nil, err
		}
		whole.Software = append(whole.Software, softwareRecord{Software: *read.JSON200, History: *history.JSON200})
	}

	pages, err := c.ListPagesWithResponse(ctx, &api.ListPagesParams{})
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(pages.HTTPResponse, pages.Body); err != nil {
		return nil, err
	}

	for _, summary := range sorted(*pages.JSON200, func(p api.PageSummary) string { return p.Slug }) {
		read, err := c.ReadPageWithResponse(ctx, summary.Slug)
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(read.HTTPResponse, read.Body); err != nil {
			return nil, err
		}
		history, err := c.ReadPageHistoryWithResponse(ctx, summary.Slug)
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(history.HTTPResponse, history.Body); err != nil {
			return nil, err
		}
		whole.Pages = append(whole.Pages, pageRecord{Page: *read.JSON200, History: *history.JSON200})
	}

	return whole, nil
}

func oneMachine(ctx context.Context, c *client.Client, key string, installed []string) (*machineRecord, error) {
	read, err := c.ReadMachineWithResponse(ctx, key)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(read.HTTPResponse, read.Body); err != nil {
		return nil, err
	}

	history, err := c.ReadMachineHistoryWithResponse(ctx, key)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(history.HTTPResponse, history.Body); err != nil {
		return nil, err
	}

	files, err := filesOf(ctx, c, anchor{kind: api.AnchorKindMachine, key: key})
	if err != nil {
		return nil, err
	}

	machine := &machineRecord{
		Machine:       *read.JSON200,
		Installations: []installationRecord{},
		Files:         files,
		History:       *history.JSON200,
	}

	sort.Strings(installed)
	for _, one := range installed {
		installation, err := oneInstallation(ctx, c, one)
		if err != nil {
			return nil, err
		}
		machine.Installations = append(machine.Installations, *installation)
	}

	return machine, nil
}

func oneInstallation(ctx context.Context, c *client.Client, key string) (*installationRecord, error) {
	read, err := c.ReadInstallationWithResponse(ctx, key)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(read.HTTPResponse, read.Body); err != nil {
		return nil, err
	}

	history, err := c.ReadInstallationHistoryWithResponse(ctx, key)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(history.HTTPResponse, history.Body); err != nil {
		return nil, err
	}

	listed, err := c.ListDeploymentsWithResponse(ctx, key)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(listed.HTTPResponse, listed.Body); err != nil {
		return nil, err
	}

	// The list is slim and carries no note, and the note is the why: every
	// deployment is read whole. An export is complete and rare, which is what
	// pays for that.
	numbers := sorted(*listed.JSON200, func(d api.DeploymentSummary) string { return fmt.Sprintf("%09d", d.Number) })
	deployments := make([]api.Deployment, 0, len(numbers))
	for _, summary := range numbers {
		one, err := c.ReadDeploymentWithResponse(ctx, key, summary.Number)
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(one.HTTPResponse, one.Body); err != nil {
			return nil, err
		}
		deployments = append(deployments, *one.JSON200)
	}

	files, err := filesOf(ctx, c, anchor{kind: api.AnchorKindInstallation, key: key})
	if err != nil {
		return nil, err
	}

	return &installationRecord{
		Installation: *read.JSON200,
		Deployments:  deployments,
		Files:        files,
		History:      *history.JSON200,
	}, nil
}

// filesOf reads every file of an owner with its content: an export is what the
// machine runs, and a list of paths would not be that.
func filesOf(ctx context.Context, c *client.Client, owner anchor) ([]api.File, error) {
	listed, err := listFiles(ctx, c, owner)
	if err != nil {
		return nil, err
	}
	summaries, err := listed.checked()
	if err != nil {
		return nil, err
	}

	files := make([]api.File, 0, len(*summaries))
	for _, summary := range sorted(*summaries, func(f api.FileSummary) string { return f.Path }) {
		got, err := readFile(ctx, c, owner, summary.Path, nil)
		if err != nil {
			return nil, err
		}
		file, err := got.checked()
		if err != nil {
			return nil, err
		}
		files = append(files, *file)
	}
	return files, nil
}

// sorted is the one order an export has: by the address of the thing, so that
// two exports of the same record are the same bytes.
func sorted[T any](items []T, by func(T) string) []T {
	ordered := make([]T, len(items))
	copy(ordered, items)
	sort.SliceStable(ordered, func(a, b int) bool { return by(ordered[a]) < by(ordered[b]) })
	return ordered
}
