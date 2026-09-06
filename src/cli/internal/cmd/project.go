package cmd

import (
	"bytes"
	"encoding/json"
	"fmt"
	"net/http"
	"strings"

	"github.com/google/uuid"
	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

func newProject(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "project", Short: "Projects: the bracket everything belongs to, and its two switches."}
	cmd.AddCommand(newProjectCreate(g), newProjectList(g), newProjectView(g), newProjectEdit(g), newProjectDelete(g), newProjectRestore(g), newProjectAccess(g))
	return cmd
}

func newProjectAccess(g *globals) *cobra.Command {
	cmd := &cobra.Command{Use: "access", Short: "Project access: users assigned to a project. Administrators grant and revoke it."}
	cmd.AddCommand(
		&cobra.Command{Use: "list KEY", Short: "List the users assigned to a project.", Args: cobra.ExactArgs(1), RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListProjectUsersWithResponse(cmd.Context(), strings.ToUpper(args[0]))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			for _, user := range *resp.JSON200 {
				fmt.Fprintf(cmd.OutOrStdout(), "%s\t%s\t%s\n", user.Id, user.Name, user.State)
			}
			return nil
		}},
		projectAccessChange(g, true),
		projectAccessChange(g, false),
	)
	return cmd
}

func projectAccessChange(g *globals, grant bool) *cobra.Command {
	verb := "revoke"
	short := "Remove a user's project access. Administrators only."
	if grant {
		verb, short = "grant", "Grant project access to a user. Administrators only."
	}
	return &cobra.Command{Use: verb + " KEY USER_ID", Short: short, Args: cobra.ExactArgs(2), RunE: func(cmd *cobra.Command, args []string) error {
		id, err := uuid.Parse(args[1])
		if err != nil {
			return &config.UsageError{Message: "USER_ID is a UUID."}
		}
		_, c, err := g.load()
		if err != nil {
			return err
		}
		var responseBody []byte
		var response *http.Response
		if grant {
			resp, callErr := c.GrantProjectAccessWithResponse(cmd.Context(), strings.ToUpper(args[0]), id)
			if callErr != nil {
				return client.Transport(callErr)
			}
			response, responseBody = resp.HTTPResponse, resp.Body
		} else {
			resp, callErr := c.RevokeProjectAccessWithResponse(cmd.Context(), strings.ToUpper(args[0]), id)
			if callErr != nil {
				return client.Transport(callErr)
			}
			response, responseBody = resp.HTTPResponse, resp.Body
		}
		if err := client.Check(response, responseBody); err != nil {
			return err
		}
		if g.json {
			return render.JSON(cmd.OutOrStdout(), map[string]any{"project": strings.ToUpper(args[0]), "user_id": id, "access": grant})
		}
		fmt.Fprintf(cmd.OutOrStdout(), "%s access for %s %s.\n", strings.ToUpper(args[0]), id, map[bool]string{true: "granted", false: "revoked"}[grant])
		return nil
	}}
}

func printProject(g *globals, cmd *cobra.Command, project api.Project) error {
	if g.json {
		return render.JSON(cmd.OutOrStdout(), project)
	}
	render.Project(cmd.OutOrStdout(), project)
	return nil
}

func newProjectCreate(g *globals) *cobra.Command {
	var triage, review bool
	cmd := &cobra.Command{
		Use:   "create KEY NAME",
		Short: "Create a project: the key that prefixes everything in it, typed by a person and never changed.",
		Args:  cobra.ExactArgs(2),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			key := strings.ToUpper(args[0])
			body := api.CreateProjectRequest{Key: &key, Name: &args[1]}
			if triage {
				body.TriageRequired = &triage
			}
			if review {
				body.ReviewRequired = &review
			}
			resp, err := c.CreateProjectWithResponse(cmd.Context(), body)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printProject(g, cmd, *resp.JSON201)
		},
	}
	cmd.Flags().BoolVar(&triage, "triage-required", false, "only flagged issues are handed out by next")
	cmd.Flags().BoolVar(&review, "review-required", false, "every close by an agent lands in review")
	return cmd
}

func newProjectList(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "list", Short: "Every project.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.ListProjectsWithResponse(cmd.Context())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			for _, p := range *resp.JSON200 {
				render.ProjectLine(cmd.OutOrStdout(), p)
			}
			return nil
		},
	}
}

func newProjectView(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "view [KEY]", Short: "One project; the repository's own when no key is given.", Args: cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			key, err := projectArg(cfg, args)
			if err != nil {
				return err
			}
			resp, err := c.ReadProjectWithResponse(cmd.Context(), key)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printProject(g, cmd, *resp.JSON200)
		},
	}
}

func projectArg(cfg config.Config, args []string) (string, error) {
	if len(args) > 0 {
		return strings.ToUpper(args[0]), nil
	}
	return requireProject(cfg)
}

func newProjectEdit(g *globals) *cobra.Command {
	var name, triage, review, instructions string
	cmd := &cobra.Command{
		Use:   "edit KEY",
		Short: "Change the name, the switches or the instructions page; the key is immutable.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			changes := map[string]any{}
			if name != "" {
				changes["name"] = name
			}
			for flag, value := range map[string]string{"triage_required": triage, "review_required": review} {
				switch value {
				case "":
				case "true":
					changes[flag] = true
				case "false":
					changes[flag] = false
				default:
					return &config.UsageError{Message: fmt.Sprintf("--%s is true or false.", strings.ReplaceAll(flag, "_", "-"))}
				}
			}
			// `none` takes the designation away, as it does for the issue's
			// epic, parent and assignee: the slug is on the wire as null, and
			// leaving the flag out leaves the designation alone.
			switch instructions {
			case "":
			case "none":
				changes["instructions_page"] = nil
			default:
				changes["instructions_page"] = instructions
			}
			if len(changes) == 0 {
				return &config.UsageError{Message: "nothing to change: --name, --triage-required, --review-required or --instructions-page."}
			}
			body, _ := json.Marshal(changes)
			resp, err := c.ChangeProjectWithBodyWithResponse(cmd.Context(), strings.ToUpper(args[0]), "application/json", bytes.NewReader(body))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printProject(g, cmd, *resp.JSON200)
		},
	}
	cmd.Flags().StringVar(&name, "name", "", "the new name")
	cmd.Flags().StringVar(&triage, "triage-required", "", "true or false")
	cmd.Flags().StringVar(&review, "review-required", "", "true or false")
	cmd.Flags().StringVar(&instructions, "instructions-page", "", "the slug of the page every agent is handed with every ticket, or none")
	return cmd
}

func newProjectDelete(g *globals) *cobra.Command {
	var confirm string
	cmd := &cobra.Command{
		Use:   "delete KEY --confirm KEY",
		Short: "Soft-delete the project with everything in it. Administrators only; the key typed twice, never prompted for.",
		Args:  cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			key := strings.ToUpper(args[0])
			if !strings.EqualFold(confirm, key) {
				return &config.UsageError{Message: fmt.Sprintf("deleting %s takes everything in it with it; pass --confirm %s to mean it.", key, key)}
			}
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.DeleteProjectWithResponse(cmd.Context(), key)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), map[string]any{"key": key, "deleted": true})
			}
			fmt.Fprintf(cmd.OutOrStdout(), "%s deleted with everything in it; `ha project restore %s` brings it back within the grace period.\n", key, key)
			return nil
		},
	}
	cmd.Flags().StringVar(&confirm, "confirm", "", "the key again, to mean it")
	return cmd
}

func newProjectRestore(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "restore KEY", Short: "Bring a deleted project back, with everything in it.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}
			resp, err := c.RestoreProjectWithResponse(cmd.Context(), strings.ToUpper(args[0]))
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			return printProject(g, cmd, *resp.JSON200)
		},
	}
}
