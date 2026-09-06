package cmd

import (
	"bytes"
	"encoding/json"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

func newLabel(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "label", Short: "Labels: the project's schema, for an agent — with a line each saying what they mean."}
	cmd.AddCommand(newLabelList(g), newLabelCreate(g), newLabelEdit(g), newLabelDelete(g), newLabelRestore(g))
	return cmd
}

func printLabel(g *globals, cmd *cobra.Command, label api.Label) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), label)
	}
	render.Labels(cmd.OutOrStdout(), []api.Label{label})
	return nil
}

func newLabelList(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "list", Short: "Every label of the project with its group and description.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			resp, err := c.ListLabelsWithResponse(cmd.Context(), project)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.Labels(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
}

func newLabelCreate(g *globals) *cobra.Command {
	var group, description string
	cmd := &cobra.Command{
		Use: "create NAME", Short: "Create a label, optionally in a group, optionally with one line saying what it means.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			resp, err := c.CreateLabelWithResponse(cmd.Context(), project, api.CreateLabelRequest{Name: &args[0], Group: optional(group), Description: optional(description)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printLabel(g, cmd, *resp.JSON201)
		},
	}
	cmd.Flags().StringVar(&group, "group", "", "the label group; one label of a group applies at a time")
	cmd.Flags().StringVar(&description, "description", "", "one line of Markdown saying what the label means here")
	return cmd
}

func newLabelEdit(g *globals) *cobra.Command {
	var name, group, description string
	cmd := &cobra.Command{
		Use: "edit NAME", Short: "Rename, regroup (`none` leaves the group) or describe (`none` clears) a label.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			changes := map[string]any{}
			if name != "" {
				changes["name"] = name
			}
			if given, value := noneOrValue(group); given {
				changes["group"] = value
			}
			if given, value := noneOrValue(description); given {
				changes["description"] = value
			}
			if len(changes) == 0 {
				return &config.UsageError{Message: "nothing to change: --name, --group or --description."}
			}
			body, _ := json.Marshal(changes)
			resp, err := c.ChangeLabelWithBodyWithResponse(cmd.Context(), project, args[0], "application/json", bytes.NewReader(body))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printLabel(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().StringVar(&name, "name", "", "the new name")
	cmd.Flags().StringVar(&group, "group", "", "the group, or `none`")
	cmd.Flags().StringVar(&description, "description", "", "the description, or `none`")
	return cmd
}

func newLabelDelete(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "delete NAME", Short: "Soft-delete a label; it vanishes from every issue until restored.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			resp, err := c.DeleteLabelWithResponse(cmd.Context(), project, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), map[string]any{"name": args[0], "deleted": true})
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s deleted; `ha label restore %s` brings it back with its attachments.\n", args[0], args[0])
			return nil
		},
	}
}

func newLabelRestore(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "restore NAME", Short: "Bring a deleted label back, with its attachments.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}
			resp, err := c.RestoreLabelWithResponse(cmd.Context(), project, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printLabel(g, cmd, *resp.JSON200)
		},
	}
}
