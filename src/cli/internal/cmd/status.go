package cmd

import (
	"context"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/version"
)

func newStatus(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "status",
		Short: "Which instance, as whom, and where each answer came from.",
		Long: "The two questions worth asking before a write — which instance is this,\n" +
			"and what kind of thing is my token — with, for each of them, which rung of\n" +
			"the ladder answered (ADR 0005).",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error { return g.status(cmd.Context()) },
	}
}

func (g *globals) status(ctx context.Context) error {
	in, err := g.input()
	if err != nil {
		return err
	}

	// The instance is the one thing without which there is nothing to say.
	address, err := in.ResolveAddress()
	if err != nil {
		return err
	}

	token, from, tokenErr := in.ResolveToken(address)

	c, err := client.New(address, token, g.httpClient())
	if err != nil {
		return err
	}

	// Nothing here stops at the first failure: `status` is the command somebody
	// runs *because* something is wrong, and one broken answer must not take
	// the other one down with it.
	var me *api.Me
	var meErr error
	if tokenErr == nil {
		resp, err := c.ReadMeWithResponse(ctx)
		switch {
		case err != nil:
			meErr = client.Transport(err)
		default:
			meErr = client.Check(resp.HTTPResponse, resp.Body)
			me = resp.JSON200
		}
	}

	served, versionErr := g.served(ctx, c)

	if g.json {
		return render.JSON(g.out(), map[string]any{
			"instance": address,
			"version":  map[string]any{"ha": version.Version, "instance": served},
			"token":    map[string]any{"from": from, "identity": me},
		})
	}

	out := g.out()
	fmt.Fprintf(out, "instance   %s\n", address)
	switch {
	case versionErr != nil:
		fmt.Fprintf(out, "           %v\n", versionErr)
	case served != "":
		fmt.Fprintf(out, "version    ha %s, instance %s\n", version.Version, served)
	}

	switch {
	case tokenErr != nil:
		fmt.Fprintf(out, "token      none: %v\n", tokenErr)
	case meErr != nil:
		fmt.Fprintf(out, "token      from %s, and the instance did not accept it: %v\n", from, meErr)
	case me != nil:
		fmt.Fprintf(out, "token      %s, from %s\n", me.Kind, from)
		fmt.Fprintf(out, "acting as  %s%s\n", me.Name, administrator(*me))
	default:
		fmt.Fprintf(out, "token      from %s\n", from)
	}

	return nil
}

// served is the instance's own version, which `GET /version` answers without a
// token — the one read that works when the token is the thing that is wrong.
func (g *globals) served(ctx context.Context, c *client.Client) (string, error) {
	resp, err := c.ReadVersionWithResponse(ctx)
	if err != nil {
		return "", client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return "", err
	}
	if resp.JSON200 == nil {
		return "", nil
	}

	return resp.JSON200.Version, nil
}

func administrator(me api.Me) string {
	if me.Administrator {
		return ", administrator"
	}
	return ""
}
