package cmd

import (
	"time"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

const exportPageSize int32 = 200

type projectExport struct {
	ExportedAt string        `json:"exported_at"`
	Hostingaffe   string        `json:"hostingaffe"`
	Project    api.Project   `json:"project"`
	Labels     []api.Label   `json:"labels"`
	Epics      []api.Epic    `json:"epics"`
	Releases   []api.Release `json:"releases"`
	Issues     []exportIssue `json:"issues"`
}

type exportIssue struct {
	api.Issue
	History []api.HistoryEntry `json:"history"`
}

func newExport(g *globals) *cobra.Command {
	return &cobra.Command{
		Use:   "export",
		Short: "Write one readable JSON document containing everything in a project.",
		Args:  cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			if !g.json {
				return &config.UsageError{Message: "export has one format: pass --json."}
			}
			cfg, c, err := g.load()
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}

			document, err := readProjectExport(cmd, c, project)
			if err != nil {
				return err
			}
			return render.JSON(cmd.OutOrStdout(), document)
		},
	}
}

func readProjectExport(cmd *cobra.Command, c *client.Client, key string) (projectExport, error) {
	var result projectExport

	versionResponse, err := c.ReadVersionWithResponse(cmd.Context())
	if err != nil {
		return result, client.Transport(err)
	}
	if err := client.Check(versionResponse.HTTPResponse, versionResponse.Body); err != nil {
		return result, err
	}
	projectResponse, err := c.ReadProjectWithResponse(cmd.Context(), key)
	if err != nil {
		return result, client.Transport(err)
	}
	if err := client.Check(projectResponse.HTTPResponse, projectResponse.Body); err != nil {
		return result, err
	}
	labelsResponse, err := c.ListLabelsWithResponse(cmd.Context(), key)
	if err != nil {
		return result, client.Transport(err)
	}
	if err := client.Check(labelsResponse.HTTPResponse, labelsResponse.Body); err != nil {
		return result, err
	}
	epics, err := readAllEpics(cmd, c, key)
	if err != nil {
		return result, err
	}
	releases, err := readAllReleases(cmd, c, key)
	if err != nil {
		return result, err
	}
	issues, err := readAllIssues(cmd, c, key)
	if err != nil {
		return result, err
	}

	result = projectExport{
		ExportedAt: time.Now().UTC().Format("2006-01-02T15:04:05.000000Z"),
		Hostingaffe:   versionResponse.JSON200.Version, Project: *projectResponse.JSON200,
		Labels: *labelsResponse.JSON200, Epics: epics, Releases: releases, Issues: issues,
	}
	return result, nil
}

func readAllEpics(cmd *cobra.Command, c *client.Client, project string) ([]api.Epic, error) {
	result := make([]api.Epic, 0)
	var cursor *string
	status := "all"
	for {
		response, err := c.ListEpicsWithResponse(cmd.Context(), &api.ListEpicsParams{Project: &project, Status: &status, Cursor: cursor, Limit: pointer(exportPageSize)})
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(response.HTTPResponse, response.Body); err != nil {
			return nil, err
		}
		for _, summary := range response.JSON200.Items {
			full, err := c.ReadEpicWithResponse(cmd.Context(), summary.Key)
			if err != nil {
				return nil, client.Transport(err)
			}
			if err := client.Check(full.HTTPResponse, full.Body); err != nil {
				return nil, err
			}
			result = append(result, *full.JSON200)
		}
		if !response.JSON200.HasMore {
			return result, nil
		}
		cursor = response.JSON200.NextCursor
	}
}

func readAllReleases(cmd *cobra.Command, c *client.Client, project string) ([]api.Release, error) {
	response, err := c.ListReleasesWithResponse(cmd.Context(), project)
	if err != nil {
		return nil, client.Transport(err)
	}
	if err := client.Check(response.HTTPResponse, response.Body); err != nil {
		return nil, err
	}
	result := make([]api.Release, 0, len(*response.JSON200))
	for _, summary := range *response.JSON200 {
		full, err := c.ReadReleaseWithResponse(cmd.Context(), project, summary.Name)
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(full.HTTPResponse, full.Body); err != nil {
			return nil, err
		}
		result = append(result, *full.JSON200)
	}
	return result, nil
}

func readAllIssues(cmd *cobra.Command, c *client.Client, project string) ([]exportIssue, error) {
	result := make([]exportIssue, 0)
	var cursor *string
	statuses := []string{"backlog", "todo", "in_progress", "review", "done", "canceled"}
	for {
		response, err := c.ListIssuesWithResponse(cmd.Context(), &api.ListIssuesParams{Project: &project, Cursor: cursor, Limit: pointer(exportPageSize)}, repeated("status", statuses))
		if err != nil {
			return nil, client.Transport(err)
		}
		if err := client.Check(response.HTTPResponse, response.Body); err != nil {
			return nil, err
		}
		for _, summary := range response.JSON200.Items {
			full, err := c.ReadIssueWithResponse(cmd.Context(), summary.Key)
			if err != nil {
				return nil, client.Transport(err)
			}
			if err := client.Check(full.HTTPResponse, full.Body); err != nil {
				return nil, err
			}
			history, err := c.ReadHistoryWithResponse(cmd.Context(), summary.Key)
			if err != nil {
				return nil, client.Transport(err)
			}
			if err := client.Check(history.HTTPResponse, history.Body); err != nil {
				return nil, err
			}
			result = append(result, exportIssue{Issue: *full.JSON200, History: *history.JSON200})
		}
		if !response.JSON200.HasMore {
			return result, nil
		}
		cursor = response.JSON200.NextCursor
	}
}

func pointer[T any](value T) *T { return &value }
