package cmd

import (
	"encoding/json"
	"fmt"
	"strings"

	"github.com/spf13/cobra"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/client"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/collect"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/config"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/render"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/version"
)

// EnvMachine is which machine this host is, named beside the token in the
// environment file the cron reads. The token says which machine may report and
// the instance checks the two agree; what the token cannot do is tell `ha` the
// key, because a machine token reads nothing at all (ADR 0016).
const EnvMachine = "HOSTINGAFFE_MACHINE"

func newReport(g *globals) *cobra.Command {
	cmd := &cobra.Command{
		Use:     "report",
		Short:   "What a machine says about itself: collected on the host, handed in, and read back.",
		Aliases: []string{"reports"},
	}
	cmd.AddCommand(newReportCollect(g), newReportSend(g), newReportShow(g), newReportList(g))
	return cmd
}

// newReportCollect is the transparency path, and it comes first for that
// reason: what would leave the host is printed before anything leaves it. It
// needs no token and no instance and runs on a machine with no network.
func newReportCollect(g *globals) *cobra.Command {
	return &cobra.Command{
		Use: "collect", Short: "Gather what this host can say about itself and print it. Nothing is sent.", Args: cobra.NoArgs,
		RunE: func(cmd *cobra.Command, _ []string) error {
			report := collect.Collect(cmd.Context(), collect.Machine(), version.Version)

			encoder := json.NewEncoder(cmd.OutOrStdout())
			encoder.SetIndent("", "  ")
			return encoder.Encode(report)
		},
	}
}

// newReportSend is what the cron runs. A run that fails is not caught up
// afterwards: there is no buffer and no queue, because the next run is a
// quarter of an hour away and is the more current one anyway.
func newReportSend(g *globals) *cobra.Command {
	var tokenFile string
	var quiet bool
	cmd := &cobra.Command{
		Use: "send [KEY]", Short: "Gather and hand in. The machine is this host; its token says which one.", Args: cobra.MaximumNArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			key, err := g.reportingMachine(args)
			if err != nil {
				return err
			}

			c, err := g.reporting(tokenFile)
			if err != nil {
				return err
			}

			report := collect.Collect(cmd.Context(), collect.Machine(), version.Version)

			resp, err := c.HandInReportWithResponse(cmd.Context(), key, report.Body())
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}

			// A section nobody could determine is not a failure — the sign of
			// life is the point — but it is said once, on stderr, so that a
			// person running the line by hand sees it and a cron stays quiet.
			for _, missing := range report.Missing {
				fmt.Fprintf(cmd.ErrOrStderr(), "ha: %s: not determined (%s)\n", missing.Section, missing.Reason)
			}

			if quiet {
				return nil
			}
			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON201)
			}
			fmt.Fprintf(cmd.OutOrStdout(), "report %d of machine %s received %s\n",
				resp.JSON201.Number, key, resp.JSON201.ReceivedAt.Format("2006-01-02 15:04:05Z07:00"))
			return nil
		},
	}
	cmd.Flags().StringVar(&tokenFile, "token-file", "",
		"the file this machine's token lies in, mode 0600; "+config.EnvToken+" wins over it")
	cmd.Flags().BoolVar(&quiet, "quiet", false,
		"say nothing on success, so that a cron writes no mail every quarter of an hour")
	return cmd
}

// reportingMachine is which machine this host is: the argument, then the
// environment beside the token. It is not in the token, because a machine token
// reads nothing — not even its own machine.
func (g *globals) reportingMachine(args []string) (string, error) {
	if len(args) == 1 && strings.TrimSpace(args[0]) != "" {
		return strings.TrimSpace(args[0]), nil
	}

	if key := strings.TrimSpace(g.getenv(EnvMachine)); key != "" {
		return key, nil
	}

	return "", &config.UsageError{Message: fmt.Sprintf(
		"which machine is this host? Set %s beside the token, or name the key: `ha report send ex44`.", EnvMachine)}
}

// reporting is the client a host reports with. The keychain is not touched: a
// server has none, and a machine token never lives in one (ADR 0016).
func (g *globals) reporting(tokenFile string) (*client.Client, error) {
	in, err := g.input()
	if err != nil {
		return nil, err
	}
	in.ReadKeychain = nil

	address, err := in.ResolveAddress()
	if err != nil {
		return nil, err
	}

	token := strings.TrimSpace(g.getenv(config.EnvToken))
	if token == "" && strings.TrimSpace(tokenFile) != "" {
		if token, err = config.ReadTokenFile(strings.TrimSpace(tokenFile)); err != nil {
			return nil, err
		}
	}
	if token == "" {
		return nil, &config.UsageError{Message: fmt.Sprintf(
			"no machine token: put it in %s, or name the file it lies in with --token-file. "+
				"A person issues one with `ha machine token issue <key>`.", config.EnvToken)}
	}

	return client.New(address, token, g.httpClient())
}

// newReportShow is what somebody reads when they want to know how a machine was
// doing: the last report, or one of the series by its number.
func newReportShow(g *globals) *cobra.Command {
	var number int
	cmd := &cobra.Command{
		Use: "show KEY", Short: "The machine's last report, set out for a person. --number reads one of the series.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}

			var report *api.Report
			if cmd.Flags().Changed("number") {
				resp, err := c.ReadReportWithResponse(cmd.Context(), args[0], int32(number))
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
				report = resp.JSON200
			} else {
				resp, err := c.ReadLatestReportWithResponse(cmd.Context(), args[0])
				if err != nil {
					return client.Transport(err)
				}
				if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
					return err
				}
				report = resp.JSON200
			}

			if g.json {
				return render.JSON(cmd.OutOrStdout(), report)
			}
			render.Report(cmd.OutOrStdout(), *report)
			return nil
		},
	}
	cmd.Flags().IntVar(&number, "number", 0, "one report of the series rather than the last one")
	return cmd
}

// newReportList is the series: one line per report, enough to see when
// something changed and then look into that one.
func newReportList(g *globals) *cobra.Command {
	var limit, offset int
	cmd := &cobra.Command{
		Use: "list KEY", Short: "The machine's reports, newest first: when, how many containers ran, how full, how busy.", Args: cobra.ExactArgs(1),
		RunE: func(cmd *cobra.Command, args []string) error {
			_, c, err := g.load()
			if err != nil {
				return err
			}

			params := &api.ListReportsParams{}
			if cmd.Flags().Changed("limit") {
				narrowed := int32(limit)
				params.Limit = &narrowed
			}
			if cmd.Flags().Changed("offset") {
				skipped := int32(offset)
				params.Offset = &skipped
			}

			resp, err := c.ListReportsWithResponse(cmd.Context(), args[0], params)
			if err != nil {
				return client.Transport(err)
			}
			if err := client.Check(resp.HTTPResponse, resp.Body); err != nil {
				return err
			}

			if g.json {
				return render.JSON(cmd.OutOrStdout(), resp.JSON200)
			}
			if resp.JSON200.Total == 0 {
				fmt.Fprintf(cmd.ErrOrStderr(), "ha: %s has never reported. docs/operations.md says how to set that up.\n", args[0])
				return nil
			}
			render.ReportSummaries(cmd.OutOrStdout(), *resp.JSON200)
			return nil
		},
	}
	cmd.Flags().IntVar(&limit, "limit", 0, "how many at most; 50 by default and never more than 200")
	cmd.Flags().IntVar(&offset, "offset", 0, "how many to skip, to walk further back")
	return cmd
}
