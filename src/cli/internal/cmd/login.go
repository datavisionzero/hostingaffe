package cmd

import (
	"context"
	"errors"
	"fmt"
	"net/url"
	"strings"
	"time"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/keychain"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

func newLogin(g *globals) *cobra.Command {
	var tokenFile string

	cmd := &cobra.Command{
		Use:   "login",
		Short: "Sign in through a browser on any machine, and keep the token in the keychain.",
		Long: "The device-code login (ADR 0005): ha prints a short code and a person\n" +
			"approves it in a browser — on this machine or on another one. That is the\n" +
			"only sign-in that works over SSH, in CI, in a container and in an agent's\n" +
			"sandbox, where there is no browser to open.\n\n" +
			"What comes back is an ordinary user token, revocable in `ha token list`\n" +
			"like any other, and it goes into the operating system's keychain. Where\n" +
			"there is none, ha says so and names the two ways on rather than quietly\n" +
			"writing a credential to a file.\n\n" +
			"An agent's token never comes through here: it arrives in " + config.EnvToken + ".",
		Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			return g.login(cmd.Context(), strings.TrimSpace(tokenFile))
		},
	}
	cmd.Flags().StringVar(&tokenFile, "token-file", "",
		"write the token to this file instead of the keychain, readable only by you")

	return cmd
}

func (g *globals) login(ctx context.Context, tokenFile string) error {
	address, c, err := g.anonymous()
	if err != nil {
		return err
	}

	begun, err := c.BeginDeviceLoginWithResponse(ctx)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(begun.HTTPResponse, begun.Body); err != nil {
		return err
	}
	if begun.JSON200 == nil {
		return &client.Failure{Code: exit.Unexpected, Message: "the instance began a login ha cannot read"}
	}
	login := *begun.JSON200

	// Both addresses arrive relative to the instance, and for a reason the
	// instance is right about: it stands behind a proxy and would have to be
	// told its own public name to build a whole one. ha has the host already,
	// and it is the one that has to join the two halves — a path with no host
	// is not something a person can open.
	fmt.Fprintf(g.msg(), "Open %s and enter this code:\n\n    %s\n\n", at(address, login.VerificationUri), login.UserCode)
	if login.VerificationUriComplete != "" {
		fmt.Fprintf(g.msg(), "Or open the code's own address: %s\n\n", at(address, login.VerificationUriComplete))
	}
	fmt.Fprintf(g.msg(), "Waiting. The code is good for %s.\n", minutes(login.ExpiresInSeconds))

	collected, err := g.poll(ctx, c, login)
	if err != nil {
		return err
	}

	if err := g.keep(address, collected.Token.Secret, tokenFile); err != nil {
		return err
	}

	fmt.Fprintf(g.msg(), "Signed in to %s as %s.\n", address, collected.User.Name)

	if g.json {
		// The secret is what this command just put in the keychain, and
		// printing it here would put it in the terminal it was kept out of.
		return render.JSON(g.out(), map[string]any{
			"instance": address,
			"user":     collected.User,
			"token":    map[string]any{"id": collected.Token.Id, "prefix": collected.Token.Prefix, "created_at": collected.Token.CreatedAt},
		})
	}

	return nil
}

// poll collects the token once a person has approved. Which refusal comes back
// is the whole protocol: `device-pending` means keep asking, and the other
// three mean stop (docs/api.md).
func (g *globals) poll(ctx context.Context, c *client.Client, login api.BegunDeviceLogin) (api.RedeemedDeviceLogin, error) {
	interval := time.Duration(login.IntervalSeconds) * time.Second
	if interval <= 0 {
		interval = 5 * time.Second
	}
	deadline := time.Now().Add(time.Duration(login.ExpiresInSeconds) * time.Second)

	for {
		resp, err := c.RedeemDeviceLoginWithResponse(ctx, api.RedeemDeviceLoginRequest{DeviceCode: &login.DeviceCode})
		if err != nil {
			return api.RedeemedDeviceLogin{}, client.Transport(err)
		}

		checked := client.Check(resp.HTTPResponse, resp.Body)
		if checked == nil {
			if resp.JSON200 == nil {
				return api.RedeemedDeviceLogin{}, &client.Failure{
					Code: exit.Unexpected, Message: "the instance answered a login ha cannot read"}
			}
			return *resp.JSON200, nil
		}

		var failure *client.Failure
		if !errors.As(checked, &failure) || failure.Problem.Code() != "device-pending" {
			return api.RedeemedDeviceLogin{}, checked
		}

		if time.Now().After(deadline) {
			return api.RedeemedDeviceLogin{}, &client.Failure{
				Code:    exit.Denied,
				Message: "nobody approved that login in time.",
			}
		}

		select {
		case <-ctx.Done():
			return api.RedeemedDeviceLogin{}, ctx.Err()
		case <-time.After(interval):
		}
	}
}

// keep puts the token where it belongs and records where that was, so that the
// next command finds it without being told again.
func (g *globals) keep(address, secret, tokenFile string) error {
	file, err := g.readConfig()
	if err != nil {
		return err
	}
	file.Instance = address

	if tokenFile != "" {
		if err := config.WriteTokenFile(tokenFile, secret); err != nil {
			return &config.UsageError{Message: fmt.Sprintf("%s could not be written: %v", tokenFile, err)}
		}
		file.TokenFile = tokenFile
		fmt.Fprintf(g.msg(), "The token is in %s, readable only by you.\n", tokenFile)
		return g.writeConfig(file)
	}

	if err := g.keychain().Store(address, secret); err != nil {
		if errors.Is(err, keychain.ErrUnavailable) {
			return &config.UsageError{Message: keychain.Advice}
		}
		return err
	}

	file.TokenFile = ""
	return g.writeConfig(file)
}

func newLogout(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "logout",
		Short: "Revoke this machine's token and take it out of the keychain.",
		Args:  cobra.NoArgs,
		RunE:  func(cmd *cobra.Command, _ []string) error { return g.logout(cmd.Context()) },
	}
}

func (g *globals) logout(ctx context.Context) error {
	resolved, c, err := g.load()
	if err != nil {
		return err
	}

	// A token that came out of the environment is the agent's or CI's, and ha
	// did not put it there. Revoking it from here would revoke something the
	// person at this terminal may not know they are holding.
	if resolved.TokenFrom == config.EnvToken {
		return &config.UsageError{Message: fmt.Sprintf(
			"the token in use came from %s, and ha did not put it there: unset the variable, or revoke that token where it was created.",
			config.EnvToken)}
	}

	revoked := g.revoke(ctx, c)

	file, err := g.readConfig()
	if err != nil {
		return err
	}
	if file.TokenFile != "" {
		if err := config.WriteTokenFile(file.TokenFile, ""); err != nil {
			return err
		}
		file.TokenFile = ""
	}
	if err := g.keychain().Forget(resolved.Address); err != nil && !errors.Is(err, keychain.ErrUnavailable) {
		return err
	}
	if err := g.writeConfig(file); err != nil {
		return err
	}

	// A token the instance no longer knows is one this machine should stop
	// holding either: the local half of a logout is worth doing regardless, and
	// what the instance said about the other half is said afterwards.
	if revoked != nil {
		fmt.Fprintf(g.msg(), "The token is gone from this machine. The instance answered: %v\n", revoked)
		return nil
	}

	fmt.Fprintf(g.msg(), "Signed out of %s.\n", resolved.Address)
	return nil
}

// revoke asks the instance to revoke the token this invocation came in under.
// Which token that is comes from `GET /me`, because an id is what the revoke
// takes and a holder should not have to recognise its own token in a list.
func (g *globals) revoke(ctx context.Context, c *client.Client) error {
	me, err := c.ReadMeWithResponse(ctx)
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(me.HTTPResponse, me.Body); err != nil {
		return err
	}
	if me.JSON200 == nil || me.JSON200.Token == nil {
		return &client.Failure{
			Code:    exit.Unexpected,
			Message: "the instance did not say which token this is; nothing was revoked",
		}
	}

	resp, err := c.RevokeTokenWithResponse(ctx, me.JSON200.Token.Id)
	if err != nil {
		return client.Transport(err)
	}

	return client.Check(resp.HTTPResponse, resp.Body)
}

// at reads a reference the instance gave against the address ha is talking to.
// A reference that is already absolute survives untouched, so an instance that
// one day answers with a whole URL is not broken by this.
func at(address, reference string) string {
	base, err := url.Parse(address)
	if err != nil {
		return reference
	}

	ref, err := url.Parse(reference)
	if err != nil {
		return reference
	}

	return base.ResolveReference(ref).String()
}

func minutes(seconds int32) string {
	if seconds < 120 {
		return fmt.Sprintf("%d seconds", seconds)
	}
	return fmt.Sprintf("%d minutes", seconds/60)
}
