package cmd

import (
	"bytes"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// The word is uncountable, so there is no `ha softwares` and no plural alias:
// the object is `software`, once (CONTEXT.md, Software).
func newSoftware(g *globals) *cobra.Command {
	cmd := &cobra.Command{
		Use:   "software",
		Short: "Software: what the installations of this instance are installations of. It carries no version.",
	}
	cmd.AddCommand(
		newSoftwareList(g), newSoftwareView(g), newSoftwareAdd(g), newSoftwareSet(g),
		newSoftwareDelete(g), newSoftwareRestore(g), newSoftwareHistory(g))
	return cmd
}

func printSoftware(g *globals, cmd *cobra.Command, software api.Software) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), software)
	}
	render.Software(cmd.OutOrStdout(), software)
	return nil
}

func newSoftwareList(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "list", Short: "Every software, by key, without the descriptions.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListSoftwareWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.SoftwareSummaries(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
}

func newSoftwareView(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "view KEY", Short: "The software: every field, and who touched it last.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadSoftwareWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printSoftware(g, cmd, *resp.JSON200)
		},
	}
}

// softwareFields are the flags a software is written with, on `add` and `set`
// alike. There is no `--version` here, and the instance says why: versions
// belong to deployments.
type softwareFields struct {
	name, homepage, repository, image string
	description, descriptionFile      string
}

func (s *softwareFields) flags(cmd *cobra.Command) {
	f := cmd.Flags()
	f.StringVar(&s.name, "name", "", "what it is called; a software without one is called by its key")
	f.StringVar(&s.homepage, "homepage", "", "its home on the web, an http or https address")
	f.StringVar(&s.repository, "repository", "", "where its source is, an http or https address")
	f.StringVar(&s.image, "image", "", "the container image, without a tag — the tag belongs to a deployment")
	f.StringVar(&s.description, "description", "", "what does not fit in a field, in Markdown")
	f.StringVar(&s.descriptionFile, "description-file", "", "the description, from a file or `-` for stdin")
}

func (s *softwareFields) body(cmd *cobra.Command) (*fields, error) {
	f := given(cmd)
	for flag, value := range map[string]*string{
		"name": &s.name, "homepage": &s.homepage, "repository": &s.repository,
		"image": &s.image, "description": &s.description,
	} {
		f.take(flag, value)
	}

	if cmd.Flags().Changed("description") && cmd.Flags().Changed("description-file") {
		return nil, &config.UsageError{Message: "the description comes from one place: --description or --description-file, not both."}
	}

	text, err := readText(cmd.InOrStdin(), s.descriptionFile)
	if err != nil {
		return nil, err
	}
	if text != nil {
		f.set("description", *text)
	}

	return f, nil
}

func newSoftwareAdd(g *globals) *cobra.Command {
	var write softwareFields
	var note string
	cmd := &cobra.Command{
		Use: "add KEY", Short: "Record a software. The key is the address you give it, and it is immutable.", Args: cobra.ExactArgs(1),
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
			resp, err := c.CreateSoftwareWithBodyWithResponse(
				cmd.Context(), &api.CreateSoftwareParams{Note: optional(note)},
				"application/json", bytes.NewReader(body.json()))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printSoftware(g, cmd, *resp.JSON201)
		},
	}
	write.flags(cmd)
	noteFlag(cmd, &note)
	return cmd
}

func newSoftwareSet(g *globals) *cobra.Command {
	var write softwareFields
	var note, ifMatch string
	cmd := &cobra.Command{
		Use: "set KEY", Short: "Change any field but the key. What is left out stays; an empty value clears a text field.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			body, err := write.body(cmd)
			if err != nil {
				return err
			}
			if body.nothing() {
				return &config.UsageError{Message: "nothing to change: name a field to set. `ha software set --help` lists them."}
			}

			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ChangeSoftwareWithBodyWithResponse(
				cmd.Context(), args[0], &api.ChangeSoftwareParams{Note: optional(note)},
				"application/json", bytes.NewReader(body.json()), guard(ifMatch))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printSoftware(g, cmd, *resp.JSON200)
		},
	}
	write.flags(cmd)
	noteFlag(cmd, &note)
	ifMatchFlag(cmd, &ifMatch)
	return cmd
}

// newSoftwareDelete is the one deletion in the record that cascades nothing: a
// software with installations on it is refused, and the refusal says how many.
func newSoftwareDelete(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{
		Use: "delete KEY", Short: "Soft-delete a software. One with installations still on it is refused, and says how many.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.DeleteSoftwareWithResponse(cmd.Context(), args[0], &api.DeleteSoftwareParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return deleted(g, cmd, "software", args[0])
		},
	}
	noteFlag(cmd, &note)
	return cmd
}

func newSoftwareRestore(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{
		Use: "restore KEY", Short: "Bring a deleted software back, under the key it kept.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RestoreSoftwareWithResponse(cmd.Context(), args[0], &api.RestoreSoftwareParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printSoftware(g, cmd, *resp.JSON200)
		},
	}
	noteFlag(cmd, &note)
	return cmd
}

func newSoftwareHistory(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "history KEY", Short: "Who changed what, oldest first, with the note that came with the change.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadSoftwareHistoryWithResponse(cmd.Context(), args[0])
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
