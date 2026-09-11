package cmd

import (
	"bytes"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// The object is the installation (CONTEXT.md); `inst` is the short form
// VISION 6.1 types and nothing more.
func newInstallation(g *globals) *cobra.Command {
	cmd := &cobra.Command{
		Use:     "installation",
		Short:   "Installations: one software installed once on one machine.",
		Aliases: []string{"inst", "installations"},
	}
	cmd.AddCommand(
		newInstallationList(g), newInstallationView(g), newInstallationAdd(g), newInstallationSet(g),
		newInstallationDelete(g), newInstallationRestore(g), newInstallationHistory(g))
	return cmd
}

func printInstallation(g *globals, cmd *cobra.Command, installation api.Installation) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), installation)
	}
	render.Installation(cmd.OutOrStdout(), installation)
	return nil
}

// newInstallationList narrows by every closed set and by both keys, because the
// question VISION 7 names — every production installation without a backup —
// has to be one call.
func newInstallationList(g *globals) *cobra.Command {
	var machine, software, environment, role, status, backup, monitoring, logging string
	var retired bool
	cmd := &cobra.Command{
		Use: "list", Short: "Every installation, by key, without the descriptions. Every flag narrows by one value.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			params := &api.ListInstallationsParams{
				Machine:     optional(machine),
				Software:    optional(software),
				Environment: optional(environment),
				Role:        optional(role),
				Status:      optional(status),
				Backup:      optional(backup),
				Monitoring:  optional(monitoring),
				Logging:     optional(logging),
			}
			if retired {
				params.Retired = &retired
			}
			resp, err := c.ListInstallationsWithResponse(cmd.Context(), params)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.InstallationSummaries(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
	f := cmd.Flags()
	f.StringVar(&machine, "machine", "", "only what is installed on this machine, by `key`")
	f.StringVar(&software, "software", "", "only installations of this software, by `key`")
	f.StringVar(&environment, "environment", "", "production, staging or development")
	f.StringVar(&role, "role", "", "application or platform")
	f.StringVar(&status, "status", "", "planned, active or retired")
	f.StringVar(&backup, "backup", "", "none, planned or active")
	f.StringVar(&monitoring, "monitoring", "", "none or external")
	f.StringVar(&logging, "logging", "", "local or central")
	f.BoolVar(&retired, "retired", false, "the retired ones as well, not only what is still there")
	return cmd
}

func newInstallationView(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "view KEY", Short: "The installation: every field, the machine and the software it names, and who touched it last.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadInstallationWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printInstallation(g, cmd, *resp.JSON200)
		},
	}
}

// installationFields are the flags an installation is written with. The three
// lists are repeated flags and are replaced whole; `--version` is on `add` only,
// because a version after that is a deployment.
type installationFields struct {
	name, machine, software, environment, role, status string
	backup, monitoring, logging, path, data            string
	description, descriptionFile                       string
	urls, secrets, ports                               []string
}

func (i *installationFields) flags(cmd *cobra.Command) {
	f := cmd.Flags()
	f.StringVar(&i.name, "name", "", "what it is called; an installation without one is called by its key")
	f.StringVar(&i.machine, "machine", "", "the machine it is installed on, by `key`")
	f.StringVar(&i.software, "software", "", "the software it is an installation of, by `key`")
	f.StringVar(&i.environment, "environment", "", "whom it serves: production, staging or development")
	f.StringVar(&i.role, "role", "", "what it is for the host: application or platform")
	f.StringVar(&i.status, "status", "", "planned, active or retired")
	f.StringVar(&i.backup, "backup", "", "none, planned or active")
	f.StringVar(&i.monitoring, "monitoring", "", "none or external")
	f.StringVar(&i.logging, "logging", "", "local or central")
	f.StringVar(&i.path, "path", "", "where it lives on the machine")
	f.StringVar(&i.data, "data", "", "where its persistent data lies, and what a backup has to take")
	f.StringArrayVar(&i.urls, "url", nil, "an address it is reachable at; repeat for more, `none` clears the list")
	f.StringArrayVar(&i.secrets, "secret", nil, "the *name* of a secret it needs, never a value; repeat for more, `none` clears the list")
	f.StringArrayVar(&i.ports, "port", nil, "a port as 443/tcp:public; repeat for more, `none` clears the list")
	f.StringVar(&i.description, "description", "", "what does not fit in a field, in Markdown")
	f.StringVar(&i.descriptionFile, "description-file", "", "the description, from a file or `-` for stdin")
}

func (i *installationFields) body(cmd *cobra.Command) (*fields, error) {
	f := given(cmd)
	for flag, value := range map[string]*string{
		"name": &i.name, "machine": &i.machine, "software": &i.software,
		"environment": &i.environment, "role": &i.role, "status": &i.status,
		"backup": &i.backup, "monitoring": &i.monitoring, "logging": &i.logging,
		"path": &i.path, "data": &i.data, "description": &i.description,
	} {
		f.take(flag, value)
	}

	if err := f.takeList("url", "urls", i.urls, asItIs); err != nil {
		return nil, err
	}
	if err := f.takeList("secret", "secrets", i.secrets, asItIs); err != nil {
		return nil, err
	}
	if err := f.takeList("port", "ports", i.ports, aPort); err != nil {
		return nil, err
	}

	if cmd.Flags().Changed("description") && cmd.Flags().Changed("description-file") {
		return nil, &config.UsageError{Message: "the description comes from one place: --description or --description-file, not both."}
	}

	text, err := readText(cmd.InOrStdin(), i.descriptionFile)
	if err != nil {
		return nil, err
	}
	if text != nil {
		f.set("description", *text)
	}

	return f, nil
}

func newInstallationAdd(g *globals) *cobra.Command {
	var write installationFields
	var note, version string
	cmd := &cobra.Command{
		Use:   "add KEY --machine KEY --software KEY --environment ENVIRONMENT --role ROLE",
		Short: "Record an installation. The key is the address you give it, and it is immutable.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			body, err := write.body(cmd)
			if err != nil {
				return err
			}
			body.set("key", args[0])
			if cmd.Flags().Changed("version") {
				body.set("version", version)
			}

			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.CreateInstallationWithBodyWithResponse(
				cmd.Context(), &api.CreateInstallationParams{Note: optional(note)},
				"application/json", bytes.NewReader(body.json()))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printInstallation(g, cmd, *resp.JSON201)
		},
	}
	write.flags(cmd)
	cmd.Flags().StringVar(&version, "version", "", "the version running now, recorded as the first deployment in the same act")
	noteFlag(cmd, &note)
	return cmd
}

// newInstallationSet has no `--version`: the version is derived from the
// deployments, and changing it is `ha deploy`, not a field write (CONTEXT.md,
// Installation).
func newInstallationSet(g *globals) *cobra.Command {
	var write installationFields
	var note, ifMatch string
	cmd := &cobra.Command{
		Use: "set KEY", Short: "Change any field but the key and the version. What is left out stays; an empty value clears a text field.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			body, err := write.body(cmd)
			if err != nil {
				return err
			}
			if body.nothing() {
				return &config.UsageError{Message: "nothing to change: name a field to set. `ha installation set --help` lists them."}
			}

			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ChangeInstallationWithBodyWithResponse(
				cmd.Context(), args[0], &api.ChangeInstallationParams{Note: optional(note)},
				"application/json", bytes.NewReader(body.json()), guard(ifMatch))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printInstallation(g, cmd, *resp.JSON200)
		},
	}
	write.flags(cmd)
	noteFlag(cmd, &note)
	ifMatchFlag(cmd, &ifMatch)
	return cmd
}

func newInstallationDelete(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{
		Use: "delete KEY", Short: "Soft-delete an installation with its files and deployments. Retiring is --status retired.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.DeleteInstallationWithResponse(cmd.Context(), args[0], &api.DeleteInstallationParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return deleted(g, cmd, "installation", args[0])
		},
	}
	noteFlag(cmd, &note)
	return cmd
}

func newInstallationRestore(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{
		Use: "restore KEY", Short: "Bring a deleted installation back, with exactly what its deletion took.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RestoreInstallationWithResponse(cmd.Context(), args[0], &api.RestoreInstallationParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printInstallation(g, cmd, *resp.JSON200)
		},
	}
	noteFlag(cmd, &note)
	return cmd
}

func newInstallationHistory(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "history KEY", Short: "Who changed what, oldest first, with the note that came with the change.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadInstallationHistoryWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.History(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
}
