package cmd

import (
	"bytes"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
	"github.com/spf13/cobra"
)

func newProvider(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "provider", Short: "Providers that host machines, by immutable key."}
	cmd.AddCommand(newProviderList(g), newProviderView(g), newProviderAdd(g), newProviderSet(g),
		newProviderDelete(g), newProviderRestore(g), newProviderHistory(g), newProviderMachines(g))
	return cmd
}

func printProvider(g *globals, cmd *cobra.Command, provider api.Provider) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), provider)
	}
	render.Provider(cmd.OutOrStdout(), provider)
	return nil
}

func newProviderList(g *globals) *cobra.Command {
	return &cobra.Command{Use: "list", Short: "Every live provider, by key.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListProvidersWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.ProviderSummaries(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		}}
}

func newProviderView(g *globals) *cobra.Command {
	return &cobra.Command{Use: "view KEY", Short: "The provider, its description and authors.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadProviderWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printProvider(g, cmd, *resp.JSON200)
		}}
}

type providerFields struct{ name, description, descriptionFile, emblem, emblemPalette string }

func (p *providerFields) flags(cmd *cobra.Command) {
	f := cmd.Flags()
	f.StringVar(&p.name, "name", "", "the provider's display name; defaults to its key")
	f.StringVar(&p.description, "description", "", "the provider's Markdown description")
	f.StringVar(&p.descriptionFile, "description-file", "", "the description from a file, or `-` for stdin")
	f.StringVar(&p.emblem, "emblem", "",
		"the composition it is recognised by: orbit, arch, peak, split, quarter, stack, wave, grid, "+
			"target, bloom, eclipse, chevron, bridge, tiles, beam or steps; empty clears it")
	f.StringVar(&p.emblemPalette, "emblem-palette", "",
		"the colours of that composition: bauhaus, ember, meadow, lagoon, dusk, citrus, orchid, granite, coral or glacier; empty clears it")
}

func (p *providerFields) body(cmd *cobra.Command) (*fields, error) {
	f := given(cmd)
	f.take("name", &p.name)
	f.take("description", &p.description)
	f.take("emblem", &p.emblem)
	f.take("emblem-palette", &p.emblemPalette)
	if cmd.Flags().Changed("description") && cmd.Flags().Changed("description-file") {
		return nil, &config.UsageError{Message: "the description comes from one place: --description or --description-file, not both."}
	}
	text, err := readText(cmd.InOrStdin(), p.descriptionFile)
	if err != nil {
		return nil, err
	}
	if text != nil {
		f.set("description", *text)
	}
	return f, nil
}

func newProviderAdd(g *globals) *cobra.Command {
	var write providerFields
	var note string
	cmd := &cobra.Command{Use: "add KEY", Short: "Record a provider with an immutable key.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			body, err := write.body(cmd)
			if err != nil {
				return err
			}
			body.set("key", args[0])
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.CreateProviderWithBodyWithResponse(cmd.Context(), &api.CreateProviderParams{Note: optional(note)}, "application/json", bytes.NewReader(body.json()))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printProvider(g, cmd, *resp.JSON201)
		}}
	write.flags(cmd)
	noteFlag(cmd, &note)
	return cmd
}

func newProviderSet(g *globals) *cobra.Command {
	var write providerFields
	var note, ifMatch string
	cmd := &cobra.Command{Use: "set KEY", Short: "Change the name, description or emblem; an empty value clears a text field.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			body, err := write.body(cmd)
			if err != nil {
				return err
			}
			if body.nothing() {
				return &config.UsageError{Message: "nothing to change: give --name, --description, --emblem or --emblem-palette."}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ChangeProviderWithBodyWithResponse(cmd.Context(), args[0], &api.ChangeProviderParams{Note: optional(note)}, "application/json", bytes.NewReader(body.json()), guard(ifMatch))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printProvider(g, cmd, *resp.JSON200)
		}}
	write.flags(cmd)
	noteFlag(cmd, &note)
	ifMatchFlag(cmd, &ifMatch)
	return cmd
}

func newProviderDelete(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{Use: "delete KEY", Short: "Soft-delete a provider that has no machines.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.DeleteProviderWithResponse(cmd.Context(), args[0], &api.DeleteProviderParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return deleted(g, cmd, "provider", args[0])
		}}
	noteFlag(cmd, &note)
	return cmd
}

func newProviderRestore(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{Use: "restore KEY", Short: "Restore a deleted provider under its reserved key.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RestoreProviderWithResponse(cmd.Context(), args[0], &api.RestoreProviderParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printProvider(g, cmd, *resp.JSON200)
		}}
	noteFlag(cmd, &note)
	return cmd
}

func newProviderHistory(g *globals) *cobra.Command {
	return &cobra.Command{Use: "history KEY", Short: "The provider's changes, oldest first.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadProviderHistoryWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.History(cmd.OutOrStdout(), *resp.JSON200, args[0])
			return nil
		}}
}

func newProviderMachines(g *globals) *cobra.Command {
	return &cobra.Command{Use: "machines KEY", Short: "Machines supplied by the provider, including VMs through their hosts.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			retired := true
			resp, err := c.ListMachinesWithResponse(cmd.Context(), &api.ListMachinesParams{Provider: &args[0], Retired: &retired})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.MachineSummaries(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		}}
}
