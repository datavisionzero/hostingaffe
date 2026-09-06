package cmd

import (
	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// `ha search "18502"` is the question a host record is asked most often: where
// was that again. It is one call over every field, every Markdown body and
// every file (VISION 5).
func newSearch(g *globals) *cobra.Command {
	var limit int32
	cmd := &cobra.Command{
		Use: "search WORDS", Short: "Where was that again: one call over every field, every Markdown body and every file.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}

			params := &api.SearchParams{Q: &args[0]}
			if cmd.Flags().Changed("limit") {
				params.Limit = &limit
			}

			resp, err := c.SearchWithResponse(cmd.Context(), params)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			render.SearchHits(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
	cmd.Flags().Int32Var(&limit, "limit", 0, "how many hits at most; the instance caps it whatever is asked")
	return cmd
}
