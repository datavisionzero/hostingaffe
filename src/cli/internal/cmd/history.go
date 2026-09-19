package cmd

import (
	"fmt"
	"regexp"
	"strconv"
	"time"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
)

// `ha history --machine ex44 --since 7d` is the question somebody — or an agent
// — asks before touching a host: what has been going on here. The instance
// serves it as one reading over every subject with the deployments among them
// (docs/api.md, The history); `ha` adds the window, which is the way a person
// asks for it.
func newHistory(g *globals) *cobra.Command {
	var machine, kind, since string
	var limit int32

	cmd := &cobra.Command{
		Use: "history", Short: "What has been going on: every change to the record, newest first, deployments among them.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			floor, err := window(since, time.Now())
			if err != nil {
				return err
			}

			_, c, err := g.load()
			if err != nil {
				return err
			}

			params := &api.ReadHistoryParams{}
			if machine != "" {
				params.Machine = &machine
			}
			if kind != "" {
				params.Kind = &kind
			}
			if cmd.Flags().Changed("limit") {
				params.Limit = &limit
			}

			var events []api.HistoryEvent

			// Without a window this is one page, because a page is what the
			// endpoint answers and a count is what the caller asked for. With
			// one it is as many as the window holds: the walk stops where the
			// events leave it, and a short page is the end of the record.
			for {
				resp, err := c.ReadHistoryWithResponse(cmd.Context(), params)
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}

				page := *resp.JSON200
				events = append(events, within(page, floor)...)

				if floor == nil || len(page) == 0 || len(within(page, floor)) < len(page) {
					break
				}

				last := page[len(page)-1].Cursor
				params.Before = &last
			}

			if g.json {
				return render.JSON(cmd.OutOrStdout(), events)
			}
			render.HistoryEvents(cmd.OutOrStdout(), events)
			return nil
		},
	}

	cmd.Flags().StringVar(&machine, "machine", "", "only what hangs on this machine: its own changes, its installations', their deployments, the files of both, the pages attached to either")
	cmd.Flags().StringVar(&kind, "kind", "", "only one kind of subject: machine, software, installation, deployment, file or page")
	cmd.Flags().StringVar(&since, "since", "", "how far back to read: 24h, 7d, 2w, a Go duration, or an RFC 3339 moment. Without it, one page")
	cmd.Flags().Int32Var(&limit, "limit", 0, "how many events per call; the instance caps it at 200 whatever is asked")
	return cmd
}

var spans = regexp.MustCompile(`^([0-9]+)([dw])$`)

// window is the moment `--since` names, or nothing where it named none. Days
// and weeks are here because they are what a person says about a host record;
// everything shorter is a Go duration, which already spells `24h` and `90m`.
func window(since string, now time.Time) (*time.Time, error) {
	if since == "" {
		return nil, nil
	}

	if parts := spans.FindStringSubmatch(since); parts != nil {
		count, err := strconv.Atoi(parts[1])
		if err != nil {
			return nil, &config.UsageError{Message: fmt.Sprintf("--since %s is not a span ha can read.", since)}
		}
		days := count
		if parts[2] == "w" {
			days = count * 7
		}
		at := now.AddDate(0, 0, -days)
		return &at, nil
	}

	if span, err := time.ParseDuration(since); err == nil {
		if span < 0 {
			return nil, &config.UsageError{Message: "--since is how far back to read, so it is not negative."}
		}
		at := now.Add(-span)
		return &at, nil
	}

	if at, err := time.Parse(time.RFC3339, since); err == nil {
		return &at, nil
	}

	return nil, &config.UsageError{
		Message: fmt.Sprintf("--since %s is neither a span like 24h, 7d or 2w nor an RFC 3339 moment.", since),
	}
}

// within is the part of a page that is still inside the window. The reading is
// newest first, so everything before the first event outside it is in.
func within(page []api.HistoryEvent, floor *time.Time) []api.HistoryEvent {
	if floor == nil {
		return page
	}

	for at, event := range page {
		if event.At.Before(*floor) {
			return page[:at]
		}
	}
	return page
}
