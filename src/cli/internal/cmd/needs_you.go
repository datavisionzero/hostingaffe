package cmd

import (
	"fmt"
	"net/http"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

func newNeedsYou(g *globals) *cobra.Command {
	var (
		cursor string
		limit  int
		wait   int
	)
	cmd := &cobra.Command{
		Use:   "needs-you",
		Short: "What only a human can resolve: questions, review, unready work and stuck blocker chains.",
		Args:  cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			waiting := cmd.Flags().Changed("wait")
			if waiting && wait <= 0 {
				return &config.UsageError{Message: "--wait must be a positive number of seconds"}
			}
			round := wait
			if round > maximumServerWait {
				round = maximumServerWait
			}
			cfg, c, err := g.loadForWait(round)
			if err != nil {
				return err
			}
			project, err := requireProject(cfg)
			if err != nil {
				return err
			}

			params := &api.ListNeedsYouParams{Cursor: optional(cursor)}
			if cmd.Flags().Changed("limit") {
				value := int32(limit)
				params.Limit = &value
			}
			resp, err := c.ListNeedsYouWithResponse(cmd.Context(), project, params)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			page := resp.JSON200
			if waiting && len(page.Items) == 0 {
				etag := resp.HTTPResponse.Header.Get("ETag")
				remaining := wait
				for {
					seconds := remaining
					if seconds > maximumServerWait {
						seconds = maximumServerWait
					}
					value := int32(seconds)
					params.Wait = &value
					params.IfNoneMatch = optional(etag)
					resp, err = c.ListNeedsYouWithResponse(cmd.Context(), project, params)
					if err != nil {
						return client.Transport(err)
					}
					if resp.HTTPResponse.StatusCode != http.StatusNotModified {
						if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
							return err
						}
						page = resp.JSON200
						break
					}
					etag = resp.HTTPResponse.Header.Get("ETag")
					if remaining <= seconds {
						if g.json {
							_ = render.JSON(cmd.OutOrStdout(), page)
						}
						return emptyResult{}
					}
					remaining -= seconds
				}
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), page)
			}
			render.NeedsYou(cmd.OutOrStdout(), page.Items)
			if len(page.Items) == 0 {
				fmt.Fprintln(cmd.OutOrStdout(), "Nothing needs you.")
			}
			// A fact about the instance, said once beside the list rather than
			// once per ticket: without an agent nothing here gets worked off,
			// and the one thing to do about it is not on the list.
			if page.Agents == 0 {
				fmt.Fprintln(cmd.ErrOrStderr(), "This instance has no active agent token, so nothing will be worked off: ha agent create <name>.")
			}
			if page.HasMore && page.NextCursor != nil {
				fmt.Fprintf(cmd.ErrOrStderr(), "%d of %d; next page: --cursor %s\n", len(page.Items), page.Total, *page.NextCursor)
			}
			return nil
		},
	}
	cmd.Flags().StringVar(&cursor, "cursor", "", "the next page, as the previous one said")
	cmd.Flags().IntVar(&limit, "limit", 50, "1 to 200")
	cmd.Flags().IntVar(&wait, "wait", 0, "wait this many seconds until something needs a human")
	return cmd
}
