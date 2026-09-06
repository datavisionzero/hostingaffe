package cmd

import (
	"bytes"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

func newMachine(g *globals) *cobra.Command {
	cmd := &cobra.Command{
		Use:     "machine",
		Short:   "Machines: the computers this instance is a record of, by the key you chose for them.",
		Aliases: []string{"machines"},
	}
	cmd.AddCommand(
		newMachineList(g), newMachineView(g), newMachineAdd(g), newMachineSet(g),
		newMachineDelete(g), newMachineRestore(g), newMachineHistory(g), newMachineContext(g))
	return cmd
}

func printMachine(g *globals, cmd *cobra.Command, machine api.Machine) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), machine)
	}
	render.Machine(cmd.OutOrStdout(), machine)
	return nil
}

func newMachineList(g *globals) *cobra.Command {
	var status, kind string
	var retired bool
	cmd := &cobra.Command{
		Use: "list", Short: "Every machine, by key, without the descriptions.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			params := &api.ListMachinesParams{Status: optional(status), Kind: optional(kind)}
			if retired {
				params.Retired = &retired
			}
			resp, err := c.ListMachinesWithResponse(cmd.Context(), params)
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
		},
	}
	cmd.Flags().StringVar(&status, "status", "", "only machines in this state: planned, active or retired")
	cmd.Flags().StringVar(&kind, "kind", "", "only machines of this kind: vps, dedicated, vm or local")
	cmd.Flags().BoolVar(&retired, "retired", false, "the retired ones as well, not only what is still there")
	return cmd
}

func newMachineView(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "view KEY", Short: "The machine: every field, the host it runs on, and who touched it last.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadMachineWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printMachine(g, cmd, *resp.JSON200)
		},
	}
}

// newMachineContext is the one call an agent makes before it touches a host:
// everything recorded about the machine, as Markdown, in the order that brings
// first what is needed first (VISION 16). The instance assembles it — the
// command is measured by what it costs in round trips, and ha prints what it
// gets.
func newMachineContext(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "context KEY", Short: "Everything recorded about the machine, as Markdown. File contents are not in it.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadMachineContextWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			fmt.Fprint(cmd.OutOrStdout(), resp.JSON200.Document)
			return nil
		},
	}
}

// machineFields are the flags a machine is written with, on `add` and on `set`
// alike. They are declared once so that the two verbs cannot drift apart, and
// each closed set names its values in the help.
type machineFields struct {
	name, hostname, kind, host, provider, plan, location string
	os, arch, cpu, memory, disk                          string
	ipv4, ipv6, privateIP, ssh, status, measuredAt       string
	description, descriptionFile                         string
}

func (m *machineFields) flags(cmd *cobra.Command) {
	f := cmd.Flags()
	f.StringVar(&m.name, "name", "", "what it is called; a machine without one is called by its key")
	f.StringVar(&m.hostname, "hostname", "", "the name it answers to on the network")
	f.StringVar(&m.kind, "kind", "", "vps, dedicated, vm or local")
	f.StringVar(&m.host, "host", "", "the machine a `vm` runs on, by key; refused on every other kind")
	f.StringVar(&m.provider, "provider", "", "who it is rented from")
	f.StringVar(&m.plan, "plan", "", "what it is rented as")
	f.StringVar(&m.location, "location", "", "where it stands")
	f.StringVar(&m.os, "os", "", "the operating system it runs")
	f.StringVar(&m.arch, "arch", "", "amd64 or arm64")
	f.StringVar(&m.cpu, "cpu", "", "the processor, in words")
	f.StringVar(&m.memory, "memory", "", "the memory, in words")
	f.StringVar(&m.disk, "disk", "", "the disks, in words — \"2×512G NVMe ZFS mirror\" is truer than a number")
	f.StringVar(&m.ipv4, "ipv4", "", "its public IPv4 address")
	f.StringVar(&m.ipv6, "ipv6", "", "its public IPv6 address")
	f.StringVar(&m.privateIP, "private-ip", "", "its address on the private network")
	f.StringVar(&m.ssh, "ssh", "", "how you reach it: the host of your ssh configuration")
	f.StringVar(&m.status, "status", "", "planned, active or retired")
	f.StringVar(&m.measuredAt, "measured-at", "", "when the hardware facts were last verified: 2026-09-05, or an RFC 3339 timestamp")
	f.StringVar(&m.description, "description", "", "what does not fit in a field, in Markdown")
	f.StringVar(&m.descriptionFile, "description-file", "", "the description, from a file or `-` for stdin")
}

// body folds the flags into what the contract takes. A flag left off leaves the
// field alone; given empty, it clears a text field.
func (m *machineFields) body(cmd *cobra.Command) (*fields, error) {
	f := given(cmd)
	for flag, value := range map[string]*string{
		"name": &m.name, "hostname": &m.hostname, "kind": &m.kind, "host": &m.host,
		"provider": &m.provider, "plan": &m.plan, "location": &m.location, "os": &m.os,
		"arch": &m.arch, "cpu": &m.cpu, "memory": &m.memory, "disk": &m.disk,
		"ipv4": &m.ipv4, "ipv6": &m.ipv6, "private-ip": &m.privateIP, "ssh": &m.ssh,
		"status": &m.status, "description": &m.description,
	} {
		f.take(flag, value)
	}

	if err := f.takeMoment("measured-at", &m.measuredAt); err != nil {
		return nil, err
	}

	if cmd.Flags().Changed("description") && cmd.Flags().Changed("description-file") {
		return nil, &config.UsageError{Message: "the description comes from one place: --description or --description-file, not both."}
	}

	text, err := readText(cmd.InOrStdin(), m.descriptionFile)
	if err != nil {
		return nil, err
	}
	if text != nil {
		f.set("description", *text)
	}

	return f, nil
}

func newMachineAdd(g *globals) *cobra.Command {
	var write machineFields
	var note, file string
	cmd := &cobra.Command{
		Use:   "add KEY --kind KIND | --file FILE",
		Short: "Record a machine, or a whole host from a file: machine, software, installations, files and first deployments in one transaction.",
		Args:  cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if file != "" {
				if len(args) > 0 {
					return &config.UsageError{
						Message: "a file says which machines it holds; --file takes no key.",
					}
				}
				return importRecord(g, cmd, file, note)
			}
			if len(args) == 0 {
				return &config.UsageError{Message: "a machine has a key: `ha machine add KEY --kind KIND`, or --file for a whole host."}
			}

			body, err := write.body(cmd)
			if err != nil {
				return err
			}
			body.set("key", args[0])

			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.CreateMachineWithBodyWithResponse(
				cmd.Context(), &api.CreateMachineParams{Note: optional(note)},
				"application/json", bytes.NewReader(body.json()))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printMachine(g, cmd, *resp.JSON201)
		},
	}
	write.flags(cmd)
	cmd.Flags().StringVar(&file, "file", "",
		"a whole host from a file, in the shape `ha export` writes, or `-` for stdin")
	noteFlag(cmd, &note)
	return cmd
}

func newMachineSet(g *globals) *cobra.Command {
	var write machineFields
	var note, ifMatch string
	cmd := &cobra.Command{
		Use: "set KEY", Short: "Change any field but the key. What is left out stays; an empty value clears a text field.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			body, err := write.body(cmd)
			if err != nil {
				return err
			}
			if body.nothing() {
				return &config.UsageError{Message: "nothing to change: name a field to set. `ha machine set --help` lists them."}
			}

			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ChangeMachineWithBodyWithResponse(
				cmd.Context(), args[0], &api.ChangeMachineParams{Note: optional(note)},
				"application/json", bytes.NewReader(body.json()), guard(ifMatch))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printMachine(g, cmd, *resp.JSON200)
		},
	}
	write.flags(cmd)
	noteFlag(cmd, &note)
	ifMatchFlag(cmd, &ifMatch)
	return cmd
}

// newMachineDelete is for mistakes. Retiring is the normal end, and it is
// `ha machine set KEY --status retired` (CONTEXT.md, Retired and deleted).
func newMachineDelete(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{
		Use: "delete KEY", Short: "Soft-delete a machine with its installations, files and deployments. Retiring is --status retired.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.DeleteMachineWithResponse(cmd.Context(), args[0], &api.DeleteMachineParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return deleted(g, cmd, "machine", args[0])
		},
	}
	noteFlag(cmd, &note)
	return cmd
}

func newMachineRestore(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{
		Use: "restore KEY", Short: "Bring a deleted machine back, with exactly what its deletion took.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RestoreMachineWithResponse(cmd.Context(), args[0], &api.RestoreMachineParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printMachine(g, cmd, *resp.JSON200)
		},
	}
	noteFlag(cmd, &note)
	return cmd
}

func newMachineHistory(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "history KEY", Short: "Who changed what, oldest first, with the note that came with the change.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadMachineHistoryWithResponse(cmd.Context(), args[0])
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
