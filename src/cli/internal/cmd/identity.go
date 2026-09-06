package cmd

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"strings"

	"github.com/google/uuid"
	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/exit"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/version"
)

// The identity verbs (ADR 0015): a user administers, an agent works. A secret
// is printed once, to stdout, and nowhere else.
func identityCommands(g *globals) []*cobra.Command {
	return []*cobra.Command{newMe(g), newVersion(g), newUser(g), newAgent(g), newToken(g)}
}

func newMe(g *globals) *cobra.Command {
	cmd := &cobra.Command{
		Use: "me", Short: "Who the token says you are: the identity, its kind, its role, the token it came in under.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadMeWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.Me(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
	cmd.AddCommand(newMeSet(g), newMeEmail(g))
	return cmd
}

func newMeEmail(g *globals) *cobra.Command {
	return &cobra.Command{Use: "email ADDRESS", Short: "Send a confirmation link to a new email address.", Args: cobra.ExactArgs(1), RunE: func(cmd *cobra.Command, args []string) error {
		_, c, err := g.load()
		if err != nil {
			return err
		}
		resp, err := c.RequestEmailChangeWithResponse(cmd.Context(), api.EmailChangeRequest{Email: &args[0]})
		if err != nil {
			return client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return err
		}
		fmt.Fprintln(cmd.OutOrStdout(), "confirmation sent")
		return nil
	}}
}

func newMeSet(g *globals) *cobra.Command {
	var kind, harness, environment, harnessVersion string
	cmd := &cobra.Command{
		Use: "set", Short: "Report stable metadata about this agent; `none` clears a field. Agent tokens only.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			body := map[string]any{}
			for flag, value := range map[string]string{"kind": kind, "harness": harness, "environment": environment, "version": harnessVersion} {
				if cmd.Flags().Changed(flag) {
					if value == "none" {
						body[flag] = nil
					} else {
						body[flag] = value
					}
				}
			}
			if len(body) == 0 {
				return &config.UsageError{Message: "nothing to report: pass --kind, --harness, --environment or --version."}
			}
			encoded, err := json.Marshal(body)
			if err != nil {
				return err
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReportAgentMetadataWithBodyWithResponse(cmd.Context(), "application/json", bytes.NewReader(encoded))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.Me(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
	cmd.Flags().StringVar(&kind, "kind", "", "what the agent is; `none` clears")
	cmd.Flags().StringVar(&harness, "harness", "", "what it runs in; `none` clears")
	cmd.Flags().StringVar(&environment, "environment", "", "where it runs; `none` clears")
	cmd.Flags().StringVar(&harnessVersion, "version", "", "the harness version; `none` clears")
	return cmd
}

// newVersion prints both sides — and, when they do not fit, says so with exit
// 9, the way every other command would have on its first request (ADR 0011).
func newVersion(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "version", Short: "ha's version and the instance's, and whether the two fit.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ReadVersionWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			checked := client.Check(resp.HTTPResponse, resp.Body)
			server := ""
			if resp.JSON200 != nil {
				server = resp.JSON200.Version
			}
			if g.json {
				if err := render.JSON(cmd.OutOrStdout(), map[string]string{"ha": version.Version, "hostingaffe": server}); err != nil {
					return err
				}
			} else {
				fmt.Fprintf(cmd.OutOrStdout(), "ha %s\nhostingaffe %s\n", version.Version, server)
			}
			var failure *client.Failure
			if errors.As(checked, &failure) && failure.Code == exit.Skew {
				return failure
			}
			return checked
		},
	}
}

func newUser(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "user", Short: "Users: the humans. Administrators only create them."}
	var administrator bool
	var email string
	create := &cobra.Command{
		Use: "create NAME", Short: "Invite a user by email.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			body := api.CreateUserRequest{Name: &args[0], Email: &email}
			if administrator {
				body.Administrator = &administrator
			}
			resp, err := c.CreateUserWithResponse(cmd.Context(), body)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON201)
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s invited at %s%s\n", resp.JSON201.Name, resp.JSON201.Email, adminSuffix(resp.JSON201.Administrator))
			return nil
		},
	}
	create.Flags().BoolVar(&administrator, "administrator", false, "administers the instance: users, agents and tokens")
	create.Flags().StringVar(&email, "email", "", "email address to receive the invitation (required)")
	_ = create.MarkFlagRequired("email")
	list := &cobra.Command{
		Use: "list", Short: "Every user.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListUsersWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			for _, u := range *resp.JSON200 {
				fmt.Fprintf(cmd.OutOrStdout(), "%-36s %-24s %-12s %s%s\n", u.Id, u.Name, u.State, u.Email, adminSuffix(u.Administrator))
			}
			return nil
		},
	}
	resend := userLifecycleCommand(g, "resend USER", "Replace and resend an invited user's invitation. The user by name or id.", func(c *client.Client, cmd *cobra.Command, user string) (*http.Response, []byte, error) {
		resp, err := c.ResendInvitationWithResponse(cmd.Context(), user)
		if err != nil {
			return nil, nil, err
		}
		return resp.HTTPResponse, resp.Body, nil
	}, "invitation resent")
	deactivate := userLifecycleCommand(g, "deactivate USER", "Deactivate a user, by name or id, and suspend every way they authenticate.", func(c *client.Client, cmd *cobra.Command, user string) (*http.Response, []byte, error) {
		resp, err := c.DeactivateUserWithResponse(cmd.Context(), user)
		if err != nil {
			return nil, nil, err
		}
		return resp.HTTPResponse, resp.Body, nil
	}, "deactivated")
	reactivate := userLifecycleCommand(g, "reactivate USER", "Reactivate a deactivated user, by name or id.", func(c *client.Client, cmd *cobra.Command, user string) (*http.Response, []byte, error) {
		resp, err := c.ReactivateUserWithResponse(cmd.Context(), user)
		if err != nil {
			return nil, nil, err
		}
		return resp.HTTPResponse, resp.Body, nil
	}, "reactivated")
	var role bool
	roleCommand := &cobra.Command{Use: "administrator USER", Short: "Grant or revoke the administrator role. The user by name or id.", Args: cobra.ExactArgs(1), RunE: func(cmd *cobra.Command, args []string) error {
		_, c, err := g.load()
		if err != nil {
			return err
		}
		resp, err := c.ChangeUserAdministratorWithResponse(cmd.Context(), args[0], api.ChangeAdministratorRequest{Administrator: &role})
		if err != nil {
			return client.Transport(err)
		}
		if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
			return err
		}
		if g.json {
			return render.JSON(cmd.OutOrStdout(), resp.JSON200)
		}
		fmt.Fprintf(cmd.OutOrStdout(), "%s administrator=%t\n", args[0], role)
		return nil
	}}
	roleCommand.Flags().BoolVar(&role, "enabled", true, "whether the user administers the instance")
	cmd.AddCommand(create, list, resend, deactivate, reactivate, roleCommand)
	return cmd
}

func userLifecycleCommand(g *globals, use, short string, call func(*client.Client, *cobra.Command, string) (*http.Response, []byte, error), result string) *cobra.Command {
	return &cobra.Command{Use: use, Short: short, Args: cobra.ExactArgs(1), RunE: func(cmd *cobra.Command, args []string) error {
		_, c, err := g.load()
		if err != nil {
			return err
		}
		response, body, err := call(c, cmd, args[0])
		if err != nil {
			return client.Transport(err)
		}
		if err := client.Check(response, body); err != nil {
			return err
		}
		fmt.Fprintf(cmd.OutOrStdout(), "%s %s\n", args[0], result)
		return nil
	}}
}

func adminSuffix(administrator bool) string {
	if administrator {
		return "  administrator"
	}
	return ""
}

func newAgent(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "agent", Short: "Agents: the identities runs work under, each with exactly one token."}
	var name string
	create := &cobra.Command{
		Use: "create [--name NAME]", Short: "Create an agent and its token, printed once. A name is assigned when none is given.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.CreateAgentWithResponse(cmd.Context(), api.CreateAgentRequest{Name: optional(name)})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON201)
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s created, owned by %s\ntoken: %s\n", resp.JSON201.Name, resp.JSON201.Owner.Name, resp.JSON201.Token.Secret)
			fmt.Fprintln(cmd.ErrOrStderr(), "ha: the token is shown once; give it to one agent only — every agent its own (VISION 12).")
			return nil
		},
	}
	create.Flags().StringVar(&name, "name", "", "the agent's name; two words and a number when left out")
	list := &cobra.Command{
		Use: "list", Short: "Every agent with its owner and its token, revoked ones included.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListAgentsWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.Agents(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
	view := &cobra.Command{
		Use: "view AGENT", Short: "One agent by name or id, with its owner, token state and last reported metadata.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListAgentsWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			for _, agent := range *resp.JSON200 {
				if agent.Id.String() == args[0] || strings.EqualFold(agent.Name, args[0]) {
					if g.json {
						return render.JSON(cmd.OutOrStdout(), agent)
					}
					render.Agent(cmd.OutOrStdout(), agent)
					return nil
				}
			}
			return &client.Failure{Code: exit.NotFound, Message: fmt.Sprintf("no agent %s", args[0])}
		},
	}
	var newName string
	rename := &cobra.Command{
		Use: "rename AGENT --name NAME", Short: "Rename an agent, by name or id; the history keeps the id, so old entries show the new name.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			if newName == "" {
				return &config.UsageError{Message: "--name NAME: the new name."}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RenameAgentWithResponse(cmd.Context(), args[0], api.RenameAgentRequest{Name: &newName})
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s renamed to %s\n", args[0], resp.JSON200.Name)
			return nil
		},
	}
	rename.Flags().StringVar(&newName, "name", "", "the new name")
	revoke := &cobra.Command{
		Use: "revoke AGENT", Short: "Revoke an agent's token, by name or id. The identity stays, naming the agent in everything it did.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RevokeAgentWithResponse(cmd.Context(), args[0])
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), map[string]any{"agent": args[0], "revoked": true})
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s revoked\n", args[0])
			return nil
		},
	}
	cmd.AddCommand(create, list, view, rename, revoke)
	return cmd
}

func newToken(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "token", Short: "Your own user tokens: as many as you create, each revocable on its own."}
	create := &cobra.Command{
		Use: "create", Short: "A further token for you, printed once.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.CreateTokenWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON201)
			}
			fmt.Fprintf(cmd.OutOrStdout(), "token: %s\n", resp.JSON201.Secret)
			fmt.Fprintln(cmd.ErrOrStderr(), "ha: the token is shown once.")
			return nil
		},
	}
	list := &cobra.Command{
		Use: "list", Short: "Your tokens, revoked ones included.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListTokensWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			for _, t := range *resp.JSON200 {
				state := "active"
				if t.RevokedAt != nil {
					state = "revoked " + t.RevokedAt.Format("2006-01-02")
				}
				fmt.Fprintf(cmd.OutOrStdout(), "%-36s %s…  %s  %s\n", t.Id, t.Prefix, t.CreatedAt.Format("2006-01-02"), state)
			}
			return nil
		},
	}
	revoke := &cobra.Command{
		Use: "revoke ID", Short: "Revoke one of your own tokens.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			id, err := uuid.Parse(args[0])
			if err != nil {
				return &config.UsageError{Message: fmt.Sprintf("%q is not a token id; `ha token list` prints them.", args[0])}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RevokeTokenWithResponse(cmd.Context(), id)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), map[string]any{"id": id, "revoked": true})
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s revoked\n", args[0])
			return nil
		},
	}
	cmd.AddCommand(create, list, revoke)
	return cmd
}
