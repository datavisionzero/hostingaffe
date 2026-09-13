package cmd

import (
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// newMachineToken is the key a machine holds so that it can report for itself,
// and nothing else (ADR 0016). Issuing and revoking are a person's; an agent
// may look, because there is no secret in what it sees.
func newMachineToken(g *globals) *cobra.Command {
	cmd := &cobra.Command{
		Use:   "token",
		Short: "The key a machine reports under: it hands in a report for that machine and reads nothing.",
	}
	cmd.AddCommand(newMachineTokenIssue(g), newMachineTokenShow(g), newMachineTokenRevoke(g))
	return cmd
}

func newMachineTokenIssue(g *globals) *cobra.Command {
	var rotate bool
	var note string
	cmd := &cobra.Command{
		Use: "issue KEY", Short: "Issue the machine's token. The secret is printed once and is not stored anywhere ha can read.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			params := &api.IssueMachineTokenParams{Note: optional(note)}
			if rotate {
				params.Rotate = &rotate
			}
			resp, err := c.IssueMachineTokenWithResponse(cmd.Context(), args[0], params)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON201)
			}

			// The secret on stdout and the word about it on stderr: what is
			// piped into a file is the token, and the sentence beside it is not
			// a comment somebody has to strip out.
			fmt.Fprintf(cmd.OutOrStdout(), "token: %s\n", resp.JSON201.Secret)
			fmt.Fprintf(cmd.ErrOrStderr(),
				"ha: the token is shown once. It belongs on %s in a file of its own, mode 0600, owned by whoever runs the cron — see docs/operations.md.\n",
				resp.JSON201.Machine)
			return nil
		},
	}
	cmd.Flags().BoolVar(&rotate, "rotate", false, "replace a token the machine already has; the old one is revoked at once and the host stops reporting until it carries the new one")
	noteFlag(cmd, &note)
	return cmd
}

// newMachineTokenShow answers "is this machine still reporting, and is that the
// token's doing". It never shows a secret: only the hash is kept.
func newMachineTokenShow(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "show KEY", Short: "Whether the machine has a token, its prefix, who issued it, and when it was last used.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadMachineTokenWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.MachineToken(cmd.OutOrStdout(), args[0], *resp.JSON200)
			return nil
		},
	}
}

func newMachineTokenRevoke(g *globals) *cobra.Command {
	var note string
	cmd := &cobra.Command{
		Use: "revoke KEY", Short: "Take the machine's token back. It stops reporting at its next run, and the row stays.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RevokeMachineTokenWithResponse(cmd.Context(), args[0], &api.RevokeMachineTokenParams{Note: optional(note)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			fmt.Fprintf(cmd.OutOrStdout(), "token of machine %s revoked\n", args[0])
			return nil
		},
	}
	noteFlag(cmd, &note)
	return cmd
}
