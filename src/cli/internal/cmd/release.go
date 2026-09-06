package cmd

import (
	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

func newRelease(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "release", Short: "Releases record what shipped together; the open release fills itself."}
	cmd.AddCommand(newReleaseList(g), newReleaseView(g), newReleasePublish(g), newReleaseNotes(g),
		newReleaseAdd(g), newReleaseRemove(g), newReleaseRename(g), newReleaseRetract(g))
	return cmd
}

func projectAndClient(g *globals) (string, *client.Client, error) {
	cfg, c, err := g.load()
	if err != nil {
		return "", nil, err
	}
	project, err := requireProject(cfg)
	return project, c, err
}

func newReleaseList(g *globals) *cobra.Command {
	return &cobra.Command{Use: "list", Short: "List the open release first, then published releases newest first.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			project, c, err := projectAndClient(g)
			if err != nil {
				return err
			}
			resp, err := c.ListReleasesWithResponse(cmd.Context(), project)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.Releases(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		}}
}

func readRelease(cmd *cobra.Command, g *globals, name string) (*api.Release, error) {
	project, c, err := projectAndClient(g)
	if err != nil {
		return nil, err
	}
	resp, err := c.ReadReleaseWithResponse(cmd.Context(), project, name)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return nil, err
	}
	return resp.JSON200, nil
}

func newReleaseView(g *globals) *cobra.Command {
	return &cobra.Command{Use: "view NAME", Short: "Show one release and the issues that shipped in it; unreleased names the open one.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			release, err := readRelease(cmd, g, args[0])
			if err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), release)
			}
			render.Release(cmd.OutOrStdout(), *release)
			return nil
		}}
}

func newReleasePublish(g *globals) *cobra.Command {
	var descriptionFile string
	cmd := &cobra.Command{Use: "publish NAME", Short: "Name and freeze the open release, then create the next one.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			project, c, err := projectAndClient(g)
			if err != nil {
				return err
			}
			description, err := readText(cmd.InOrStdin(), descriptionFile)
			if err != nil {
				return err
			}
			body := api.PublishReleaseRequest{Name: &args[0], Description: description}
			resp, err := c.PublishReleaseWithResponse(cmd.Context(), project, body)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON201)
			}
			render.Release(cmd.OutOrStdout(), *resp.JSON201)
			return nil
		}}
	cmd.Flags().StringVar(&descriptionFile, "description-file", "", "release notes as Markdown, from a file or `-` for stdin")
	return cmd
}

func newReleaseNotes(g *globals) *cobra.Command {
	var descriptionFile string
	cmd := &cobra.Command{Use: "notes NAME", Short: "Print release notes as Markdown; with --description-file, annotate them first.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if descriptionFile != "" {
				project, c, err := projectAndClient(g)
				if err != nil {
					return err
				}
				description, err := readText(cmd.InOrStdin(), descriptionFile)
				if err != nil {
					return err
				}
				resp, err := c.ChangeReleaseWithResponse(cmd.Context(), project, args[0], api.ChangeReleaseRequest{Description: description})
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
				if g.json {
					return render.JSON(cmd.OutOrStdout(), resp.JSON200)
				}
				render.ReleaseNotes(cmd.OutOrStdout(), *resp.JSON200)
				return nil
			}
			release, err := readRelease(cmd, g, args[0])
			if err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), release)
			}
			render.ReleaseNotes(cmd.OutOrStdout(), *release)
			return nil
		}}
	cmd.Flags().StringVar(&descriptionFile, "description-file", "", "replace the release's Markdown annotation, from a file or `-` for stdin")
	return cmd
}

// The one release that is not a record yet, and the only one either of the two
// following verbs writes to.
const openRelease = "unreleased"

func newReleaseAdd(g *globals) *cobra.Command {
	return &cobra.Command{Use: "add KEY", Short: "Put an issue into the open release by hand; a ticket that shipped stays where it shipped.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			project, c, err := projectAndClient(g)
			if err != nil {
				return err
			}
			resp, err := c.AddIssueToReleaseWithResponse(cmd.Context(), project, openRelease, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.Release(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		}}
}

func newReleaseRemove(g *globals) *cobra.Command {
	return &cobra.Command{Use: "remove KEY", Short: "Take an issue out of the open release: it has not shipped yet and does not belong.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			project, c, err := projectAndClient(g)
			if err != nil {
				return err
			}
			resp, err := c.RemoveIssueFromReleaseWithResponse(cmd.Context(), project, openRelease, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.Release(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		}}
}

func newReleaseRename(g *globals) *cobra.Command {
	return &cobra.Command{Use: "rename NAME NEW", Short: "Correct the name of the newest publication; an older one is a record and keeps its own.", Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			project, c, err := projectAndClient(g)
			if err != nil {
				return err
			}
			resp, err := c.ChangeReleaseWithResponse(cmd.Context(), project, args[0], api.ChangeReleaseRequest{Name: &args[1]})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.Release(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		}}
}

func newReleaseRetract(g *globals) *cobra.Command {
	return &cobra.Command{Use: "retract NAME", Short: "Take the newest publication back; it is the open release again and the tickets stay in it.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			project, c, err := projectAndClient(g)
			if err != nil {
				return err
			}
			resp, err := c.RetractReleaseWithResponse(cmd.Context(), project, args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.Release(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		}}
}
