package cmd

import (
	"bytes"
	"strconv"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// The object is the deployment (CONTEXT.md); `deploy` is the short form
// VISION 6.1 types, and recording one is the bare verb, because that is what an
// agent does after the work: `ha deploy logaffe-prod --version 1.4.0`.
func newDeployment(g *globals) *cobra.Command {
	var write deploymentFields
	cmd := &cobra.Command{
		Use:     "deployment KEY --version VERSION",
		Short:   "Deployments: the record that an installation changed version. Recording one is the bare verb.",
		Aliases: []string{"deploy", "deployments"},
		Args:    cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if len(args) == 0 {
				return cmd.Help()
			}
			return recordDeployment(g, cmd, args[0], &write)
		},
	}
	write.flags(cmd)
	cmd.Flags().StringVar(&write.version, "version", "", "the version that is now running; the one field a deployment must have")
	cmd.AddCommand(
		newDeploymentList(g), newDeploymentView(g), newDeploymentSet(g),
		newDeploymentDelete(g), newDeploymentRestore(g), newDeploymentHistory(g))
	return cmd
}

func printDeployment(g *globals, cmd *cobra.Command, deployment api.Deployment) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), deployment)
	}
	render.Deployment(cmd.OutOrStdout(), deployment)
	return nil
}

// deploymentFields are what a deployment is written with. `version` is on the
// recording verb only, and `set` carries its own so that the instance can say
// why it is refused rather than ha turning it away as an unknown flag.
type deploymentFields struct {
	version, ref, at, ticket string
	note, noteFile           string
}

func (d *deploymentFields) flags(cmd *cobra.Command) {
	f := cmd.Flags()
	f.StringVar(&d.ref, "ref", "", "what was actually deployed: an image digest, a commit, a tag")
	f.StringVar(&d.at, "at", "", "when it happened, if not now: 2026-09-05, or an RFC 3339 timestamp")
	f.StringVar(&d.ticket, "ticket", "", "the planaffe ticket it was done for")
	f.StringVar(&d.note, "note", "", "why, what was checked, what went wrong — the deployment's own field, in Markdown")
	f.StringVar(&d.noteFile, "note-file", "", "the note, from a file or `-` for stdin")
}

func (d *deploymentFields) body(cmd *cobra.Command) (*fields, error) {
	f := given(cmd)
	for flag, value := range map[string]*string{
		"version": &d.version, "ref": &d.ref, "ticket": &d.ticket, "note": &d.note,
	} {
		f.take(flag, value)
	}

	if err := f.takeMoment("at", &d.at); err != nil {
		return nil, err
	}

	if cmd.Flags().Changed("note") && cmd.Flags().Changed("note-file") {
		return nil, &config.UsageError{Message: "the note comes from one place: --note or --note-file, not both."}
	}

	text, err := readText(cmd.InOrStdin(), d.noteFile)
	if err != nil {
		return nil, err
	}
	if text != nil {
		f.set("note", *text)
	}

	return f, nil
}

func recordDeployment(g *globals, cmd *cobra.Command, key string, write *deploymentFields) error {
	body, err := write.body(cmd)
	if err != nil {
		return err
	}

	_, c, err := g.load()
	if err != nil {
		return err
	}
	resp, err := c.RecordDeploymentWithBodyWithResponse(
		cmd.Context(), key, "application/json", bytes.NewReader(body.json()))
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}
	return printDeployment(g, cmd, *resp.JSON201)
}

// newDeploymentList names the installation with a flag rather than a position,
// because the position is the recording verb's: `ha deploy KEY` is what an
// agent types after the work, and `list` does not compete with it.
func newDeploymentList(g *globals) *cobra.Command {
	var installation string
	cmd := &cobra.Command{
		Use: "list --inst KEY", Short: "Every deployment of the installation, newest by `at` first.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			if installation == "" {
				return &config.UsageError{Message: "a deployment lives under an installation: --inst KEY."}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListDeploymentsWithResponse(cmd.Context(), installation)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.DeploymentSummaries(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
	cmd.Flags().StringVar(&installation, "inst", "", "the installation, by `key`")
	return cmd
}

func newDeploymentView(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "view KEY NUMBER", Short: "The deployment, with the version before it and the file revisions that were current when it went live.", Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			number, err := aNumber(args[1])
			if err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadDeploymentWithResponse(cmd.Context(), args[0], number)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printDeployment(g, cmd, *resp.JSON200)
		},
	}
}

// newDeploymentSet is narrow, and the border is the instance's: `ref`, `at`,
// `ticket` and `note`. `--version` is here so that the attempt is answered by
// the instance, which says the rule — a deployment with the wrong version is
// deleted and recorded again — rather than by ha turning the flag away.
func newDeploymentSet(g *globals) *cobra.Command {
	var write deploymentFields
	var ifMatch string
	cmd := &cobra.Command{
		Use: "set KEY NUMBER", Short: "Correct ref, at, ticket or note. The version and the installation are what the record is.", Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			number, err := aNumber(args[1])
			if err != nil {
				return err
			}
			body, err := write.body(cmd)
			if err != nil {
				return err
			}
			if body.nothing() {
				return &config.UsageError{Message: "nothing to correct: --ref, --at, --ticket, --note or --note-file."}
			}

			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.CorrectDeploymentWithBodyWithResponse(
				cmd.Context(), args[0], number, "application/json", bytes.NewReader(body.json()), guard(ifMatch))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printDeployment(g, cmd, *resp.JSON200)
		},
	}
	write.flags(cmd)
	cmd.Flags().StringVar(&write.version, "version", "", "refused: the version is what the record is — delete it and record it again")
	ifMatchFlag(cmd, &ifMatch)
	return cmd
}

// newDeploymentDelete is the other half of the correction rule: a deployment
// recorded with the wrong version is deleted and recorded again. Its number is
// not handed out a second time.
func newDeploymentDelete(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "delete KEY NUMBER", Short: "Soft-delete a deployment. Its number is not handed out again.", Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			number, err := aNumber(args[1])
			if err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.DeleteDeploymentWithResponse(cmd.Context(), args[0], number)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), map[string]any{
					"installation": args[0], "number": number, "deleted": true})
			}
			_, _ = cmd.OutOrStdout().Write([]byte(
				args[0] + " #" + args[1] + " deleted; `ha deploy restore " + args[0] + " " + args[1] + "` brings it back.\n"))
			return nil
		},
	}
}

func newDeploymentRestore(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "restore KEY NUMBER", Short: "Bring a deleted deployment back, under the number it kept.", Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			number, err := aNumber(args[1])
			if err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RestoreDeploymentWithResponse(cmd.Context(), args[0], number)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printDeployment(g, cmd, *resp.JSON200)
		},
	}
}

// newDeploymentHistory is the corrections made to one deployment. The
// deployments themselves are not history entries — they are records of their
// own (CONTEXT.md, Deployment).
func newDeploymentHistory(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "history KEY NUMBER", Short: "Every correction made to the deployment, oldest first.", Args: cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			number, err := aNumber(args[1])
			if err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadDeploymentHistoryWithResponse(cmd.Context(), args[0], number)
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

// aNumber is the address of a deployment: the instance numbers it per
// installation, and anything that is not one of those numbers is a usage
// mistake said before any request goes out.
func aNumber(text string) (int32, error) {
	number, err := strconv.ParseInt(text, 10, 32)
	if err != nil || number < 1 {
		return 0, &config.UsageError{
			Message: "a deployment is addressed by its number, counting from 1: `ha deploy list --inst KEY` says which.",
		}
	}
	return int32(number), nil
}
