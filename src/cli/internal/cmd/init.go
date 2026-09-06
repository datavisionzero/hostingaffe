package cmd

import (
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"regexp"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// projectKey is the rule of CONTEXT.md, the same one the instance enforces:
// upper case, a letter first, two to ten characters.
var projectKey = regexp.MustCompile(`^[A-Z][A-Z0-9]{1,9}$`)

// initResult is what `--json` prints: what init did, so that an agent reads it
// rather than the prose.
type initResult struct {
	URL      string       `json:"url"`
	Version  string       `json:"version"`
	Identity initIdentity `json:"identity"`
	Project  initProject  `json:"project"`
	File     string       `json:"file"`
	Next     []string     `json:"next"`
}

type initIdentity struct {
	Name string `json:"name"`
	Kind string `json:"kind"`
}

type initProject struct {
	Key     string `json:"key"`
	Name    string `json:"name"`
	Created bool   `json:"created"`
}

// newInit is the step between "the instance runs" and "the agent works with
// it". Everything it does was possible before and everybody did it themselves
// and differently: find a token, put the two variables somewhere, create a
// project, write the `.hostingaffe` by hand.
func newInit(g *globals) *cobra.Command {
	var name string
	var force bool
	cmd := &cobra.Command{
		Use:   "init [KEY]",
		Short: "Connect this repository to an instance: check the address and the token, take or create the project, write the .hostingaffe.",
		Long: "Run in the repository that is to be connected. The key comes from the argument, or is proposed from the\n" +
			"name of the directory. Nothing here is interactive: what is missing is named as an error (VISION 6.1).",
		Args: cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}

			// The first mistake a newcomer makes is one of these two, and
			// which one it was is the whole answer.
			instance, err := c.ReadVersionWithResponse(cmd.Context())
			if err != nil {
				return &client.Failure{Code: exit.Unreachable, Message: fmt.Sprintf("HOSTINGAFFE_URL is %s, and nothing answered there: %v", cfg.URL, err)}
			}
			if instance.HTTPResponse.StatusCode != http.StatusOK {
				return &client.Failure{Code: exit.Unexpected, Message: fmt.Sprintf("HOSTINGAFFE_URL is %s, and what answered there is not a hostingaffe instance (%s at /version).", cfg.URL, instance.HTTPResponse.Status)}
			}

			me, err := c.ReadMeWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if me.HTTPResponse.StatusCode == http.StatusUnauthorized {
				return &client.Failure{Code: exit.Denied, Message: fmt.Sprintf("%s answered, but HOSTINGAFFE_TOKEN is not a token it knows; create one in Settings, or revoke and replace the one you have.", cfg.URL)}
			}
			if err := client.Check(me.HTTPResponse, me.Body); err != nil {
				return err
			}

			dir := g.dir()
			key, err := chosenKey(args, dir)
			if err != nil {
				return err
			}

			project, created, err := takeOrCreate(cmd, c, key, name, dir)
			if err != nil {
				return err
			}

			file := filepath.Join(dir, config.FileName)
			if _, err := os.Stat(file); err == nil && !force {
				return &config.UsageError{Message: fmt.Sprintf("%s exists; pass --force to overwrite it.", file)}
			}
			if err := writeProjectFile(file, project.Key); err != nil {
				return err
			}

			result := initResult{
				URL:      cfg.URL,
				Version:  instance.JSON200.Version,
				Identity: initIdentity{Name: me.JSON200.Name, Kind: string(me.JSON200.Kind)},
				Project:  initProject{Key: project.Key, Name: project.Name, Created: created},
				File:     file,
				Next: []string{
					fmt.Sprintf("Keep the two variables in your shell: export HOSTINGAFFE_URL=%s and export HOSTINGAFFE_TOKEN=<the token you used here>.", cfg.URL),
					"Copy the AGENTS.md block into the repository so an agent knows this project is tracked here: docs/agents-md.md.",
				},
			}

			if g.json {
				return render.JSON(cmd.OutOrStdout(), result)
			}
			printInit(cmd.OutOrStdout(), result)
			return nil
		},
	}
	cmd.Flags().StringVar(&name, "name", "", "the project's name where it has to be created; defaults to the directory name")
	cmd.Flags().BoolVar(&force, "force", false, "overwrite an existing "+config.FileName)
	return cmd
}

// chosenKey is the argument, or the directory name made into a key. A
// directory whose name cannot be one is an error saying so rather than a
// question nobody is there to answer.
func chosenKey(args []string, dir string) (string, error) {
	if len(args) == 1 {
		key := strings.ToUpper(strings.TrimSpace(args[0]))
		if !projectKey.MatchString(key) {
			return "", &config.UsageError{Message: fmt.Sprintf("%q is not a project key: upper case, a letter first, two to ten letters or digits.", args[0])}
		}
		return key, nil
	}

	base := strings.ToUpper(filepath.Base(dir))
	suggested := regexp.MustCompile(`[^A-Z0-9]`).ReplaceAllString(base, "")
	if len(suggested) > 10 {
		suggested = suggested[:10]
	}
	if !projectKey.MatchString(suggested) {
		return "", &config.UsageError{Message: fmt.Sprintf("no key: the directory is %q, which does not make one. Pass it: ha init KEY.", filepath.Base(dir))}
	}
	return suggested, nil
}

// takeOrCreate is deliberately not a create that fails on a taken key: running
// init twice, or against a project a colleague made, is the ordinary case.
func takeOrCreate(cmd *cobra.Command, c *client.Client, key, name, dir string) (api.Project, bool, error) {
	existing, err := c.ReadProjectWithResponse(cmd.Context(), key)
	if err != nil {
		return api.Project{}, false, client.Transport(err)
	}
	if existing.HTTPResponse.StatusCode == http.StatusOK {
		return *existing.JSON200, false, nil
	}
	if existing.HTTPResponse.StatusCode != http.StatusNotFound {
		return api.Project{}, false, client.Check(existing.HTTPResponse, existing.Body)
	}

	if name == "" {
		name = filepath.Base(dir)
	}
	created, err := c.CreateProjectWithResponse(cmd.Context(), api.CreateProjectRequest{Key: &key, Name: &name})
	if err != nil {
		return api.Project{}, false, client.Transport(err)
	}
	if err := client.Check(created.HTTPResponse, created.Body); err != nil {
		return api.Project{}, false, err
	}
	return *created.JSON201, true, nil
}

func writeProjectFile(path, key string) error {
	body := fmt.Sprintf("# The project file (CONTEXT.md): this repository is the %s project.\nproject = %s\n", key, key)
	return os.WriteFile(path, []byte(body), 0o644)
}

func printInit(out io.Writer, result initResult) {
	taken := "created"
	if !result.Project.Created {
		taken = "already there"
	}
	fmt.Fprintf(out, "Instance  %s  (hostingaffe %s)\n", result.URL, result.Version)
	fmt.Fprintf(out, "Identity  %s (%s)\n", result.Identity.Name, result.Identity.Kind)
	fmt.Fprintf(out, "Project   %s · %s (%s)\n", result.Project.Key, result.Project.Name, taken)
	fmt.Fprintf(out, "Wrote     %s\n", result.File)
	fmt.Fprintln(out, "\nStill yours to do:")
	for _, step := range result.Next {
		fmt.Fprintf(out, "  - %s\n", step)
	}
}
