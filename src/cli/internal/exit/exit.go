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
	// Usage is bad arguments, or HOSTINGAFFE_URL or HOSTINGAFFE_TOKEN unset or malformed.
	Usage = 2
	// NotFound is 404 not-found and 404 deleted.
	NotFound = 3
	// Refused is 400 validation and every 422.
	Refused = 4
	// Conflict is 409: idempotency-mismatch, email-exists, last-administrator.
	Conflict = 5
	// Stale is 412 stale.
	Stale = 6
	// Denied is 401 and 403.
	Denied = 7
	// 8 is not given away. It was `next` finding nothing, which is planaffe's
	// and not this product's, and it stays free for whatever answers "there is
	// nothing" here — so that no script has to relearn a number.

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
	// A login somebody refused, and one nobody answered in time, are both a
	// door that stayed shut — the same answer 401 and 403 are, and not the
	// "your request was wrong" that 400 usually is (ADR 0005).
	case status == 400 && (p.Code() == "device-denied" || p.Code() == "device-expired"):
		return Denied
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
