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
	switch {
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
	env  Env
	json bool
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
	root.SetVersionTemplate("ha {{.Version}}\n")

	// A usage mistake is exit 2, in the words of the flag package rather than a
	// wall of help.
	root.SetFlagErrorFunc(func(_ *cobra.Command, err error) error {
		return &config.UsageError{Message: err.Error()}
	})

	root.AddCommand(newMachine(g))
	root.AddCommand(newSoftware(g))
	root.AddCommand(newInstallation(g))
	root.AddCommand(newDeployment(g))
	root.AddCommand(newPage(g))
	root.AddCommand(identityCommands(g)...)
	return root
}

// load is what every command that talks to the instance starts with.
func (g *globals) load() (config.Config, *client.Client, error) {
	getenv := g.env.Getenv
	if getenv == nil {
		getenv = os.Getenv
	}

	cfg, err := config.Load(getenv)
	if err != nil {
		return cfg, nil, err
	}

	httpClient := g.env.HTTP
	if httpClient == nil {
		httpClient = client.Default()
	}

	c, err := client.New(cfg, httpClient)
	return cfg, c, err
}
