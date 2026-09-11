package cmd

import (
	"bytes"
	"fmt"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// `ha machine add --file batch.json` is the bulk write: documenting a host is
// one act and not thirty commands (VISION 6.1). The file is the JSON `ha export`
// writes — what of it arrives and what is read past is the instance's to say
// (docs/api.md, Importing) — and ha does not read the document, it hands it to
// the instance, which is the one place that knows what a record may hold.
func importRecord(g *globals, cmd *cobra.Command, file, note string) error {
	document, err := readContent(cmd.InOrStdin(), file)
	if err != nil {
		return err
	}

	_, c, err := g.load()
	if err != nil {
		return err
	}

	resp, err := c.ImportWithBodyWithResponse(
		cmd.Context(), &api.ImportParams{Note: optional(note)},
		"application/json", bytes.NewReader([]byte(document)))
	if err != nil {
		return client.Transport(err)
	}
	if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
		return err
	}

	if g.json {
		return render.JSON(cmd.OutOrStdout(), resp.JSON200)
	}

	made := *resp.JSON200
	fmt.Fprintf(cmd.OutOrStdout(),
		"%d machines, %d installations, %d software, %d deployments, %d files, %d pages.\n",
		made.Machines, made.Installations, made.Software, made.Deployments, made.Files, made.Pages)
	return nil
}
