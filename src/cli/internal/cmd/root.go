// Package cmd is the command tree: `ha <object> <verb>`, like gh and glab
// (VISION 6.1). Data goes to stdout, errors to stderr, and the exit code says
// what happened (docs/api.md, Exit codes of the CLI).
package cmd

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/version"
)

// Env is what a command runs in, so that a test can supply all of it.
type Env struct {
	Getenv func(string) string
	Dir    string
	Stdin  io.Reader
	Stdout io.Writer
	Stderr io.Writer
	HTTP   *http.Client
}

// Run executes args and returns the exit code. Nothing here is ever
// interactive: ha reads stdin only where a flag says so, and never prompts.
func Run(ctx context.Context, args []string, env Env) int {
	root := newRoot(env)
	root.SetArgs(args)
	root.SetIn(env.Stdin)
	root.SetOut(env.Stdout)
	root.SetErr(env.Stderr)

	if err := root.ExecuteContext(ctx); err != nil {
		return report(env.Stderr, err)
	}
	return exit.OK
}

func report(stderr io.Writer, err error) int {
	var failure *client.Failure
	var usage *config.UsageError
	var empty emptyResult
	switch {
	case errors.As(err, &empty):
		// Not an error: the reasons went to stdout, and the code says it.
		return exit.Empty
	case errors.As(err, &failure):
		fmt.Fprintln(stderr, "ha:", failure.Message)
		return failure.Code
	case errors.As(err, &usage):
		fmt.Fprintln(stderr, "ha:", usage.Message)
		return exit.Usage
	default:
		fmt.Fprintln(stderr, "ha:", err)
		return exit.Unexpected
	}
}

type globals struct {
	env     Env
	json    bool
	project string
}

func newRoot(env Env) *cobra.Command {
	g := &globals{env: env}
	root := &cobra.Command{
		Use:           "ha",
		Short:         "hostingaffe from the console: the interface for agents and console-minded humans.",
		Version:       version.Version,
		SilenceUsage:  true,
		SilenceErrors: true,
	}
	root.PersistentFlags().BoolVar(&g.json, "json", false, "print the object as the API answered it")
	root.PersistentFlags().StringVar(&g.project, "project", "", "the project key; defaults to the .hostingaffe file of this repository")
	root.SetVersionTemplate("ha {{.Version}}\n")

	// A usage mistake is exit 2, in the words of the flag package rather than a
	// wall of help.
	root.SetFlagErrorFunc(func(_ *cobra.Command, err error) error {
		return &config.UsageError{Message: err.Error()}
	})

	root.AddCommand(newInit(g), newNext(g), newNeedsYou(g), newExport(g), newIssue(g), newQuestion(g), newProject(g), newLabel(g), newEpic(g), newPage(g), newRelease(g))
	root.AddCommand(identityCommands(g)...)
	return root
}

// load is what every command that talks to the instance starts with.
func (g *globals) load() (config.Config, *client.Client, error) {
	return g.loadForWait(0)
}

func (g *globals) loadForWait(seconds int) (config.Config, *client.Client, error) {
	getenv := g.env.Getenv
	if getenv == nil {
		getenv = os.Getenv
	}
	dir := g.env.Dir
	if dir == "" {
		dir, _ = os.Getwd()
	}

	cfg, err := config.Load(getenv, dir)
	if err != nil {
		return cfg, nil, err
	}
	if g.project != "" {
		cfg.Project = g.project
	}

	httpClient := g.env.HTTP
	if httpClient == nil {
		if seconds > 0 {
			httpClient = client.ForWait(seconds)
		} else {
			httpClient = client.Default()
		}
	}

	c, err := client.New(cfg, httpClient)
	return cfg, c, err
}

// dir is the repository ha was run in.
func (g *globals) dir() string {
	if g.env.Dir != "" {
		return g.env.Dir
	}
	dir, _ := os.Getwd()
	return dir
}

// requireProject is the one thing a command in a repository never has to say.
func requireProject(cfg config.Config) (string, error) {
	if cfg.Project == "" {
		return "", &config.UsageError{Message: "no project: pass --project KEY, or put a .hostingaffe file with `project = KEY` in the repository."}
	}
	return cfg.Project, nil
}
