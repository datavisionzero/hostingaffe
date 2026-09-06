// ha: hostingaffe from the console. A client of the public API and nothing else
// (ADR 0003), built as one static binary.
package main

import (
	"context"
	"os"
	"os/signal"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/cmd"
)

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt)
	defer stop()

	os.Exit(cmd.Run(ctx, os.Args[1:], cmd.Env{
		Getenv: os.Getenv,
		Stdin:  os.Stdin,
		Stdout: os.Stdout,
		Stderr: os.Stderr,
	}))
}
