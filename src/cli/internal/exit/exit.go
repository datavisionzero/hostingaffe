// Package exit is the table of docs/api.md: the code a script branches on,
// derived from the status and the problem type so that nothing has to be
// parsed.
package exit

import "github.com/datavisionzero/hostingaffe/src/cli/internal/problem"

const (
	// OK is success: 2xx.
	OK = 0
	// Unexpected is a 500, a response ha cannot parse, or a bug in ha.
	Unexpected = 1
	// Usage is bad arguments, HOSTINGAFFE_URL or HOSTINGAFFE_TOKEN unset, or a .hostingaffe file ha cannot read.
	Usage = 2
	// NotFound is 404 not-found and 404 deleted.
	NotFound = 3
	// Refused is 400 validation and every 422.
	Refused = 4
	// Conflict is 409: claim-held, claim-lost, idempotency-mismatch.
	Conflict = 5
	// Stale is 412 stale.
	Stale = 6
	// Denied is 401 and 403.
	Denied = 7
	// Empty is `next` finding nothing: not an error of the API, but the answer a loop most often branches on.
	Empty = 8
	// Skew is a CLI too old or too new for the instance (ADR 0011).
	Skew = 9
	// Unreachable is DNS, connection refused, timeout, TLS: the instance could not be reached.
	Unreachable = 10
)

// FromResponse derives the code from a status and the problem document that
// came with it, if any.
func FromResponse(status int, p *problem.Problem) int {
	switch {
	case status >= 200 && status < 300:
		return OK
	case status == 401 || status == 403:
		return Denied
	case status == 404:
		return NotFound
	case status == 400 && p.Code() == "cursor-invalid":
		return Refused
	case status == 400 || status == 422:
		return Refused
	case status == 409:
		return Conflict
	case status == 412:
		return Stale
	default:
		return Unexpected
	}
}
