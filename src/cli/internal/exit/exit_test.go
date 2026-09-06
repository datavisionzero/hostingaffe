package exit

import (
	"testing"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/problem"
)

func TestTableOfApiMd(t *testing.T) {
	cases := []struct {
		status int
		code   string
		want   int
	}{
		{200, "", OK},
		{201, "", OK},
		{400, "validation", Refused},
		{400, "cursor-invalid", Refused},
		{401, "unauthenticated", Denied},
		{403, "forbidden", Denied},
		{403, "claim-protected", Denied},
		{404, "not-found", NotFound},
		{404, "deleted", NotFound},
		{409, "claim-held", Conflict},
		{409, "claim-lost", Conflict},
		{409, "idempotency-mismatch", Conflict},
		{412, "stale", Stale},
		{422, "transition", Refused},
		{422, "cycle", Refused},
		{500, "internal", Unexpected},
		{502, "", Unexpected},
	}
	for _, c := range cases {
		var p *problem.Problem
		if c.code != "" {
			p = problem.Parse([]byte(`{"type":"/problems/` + c.code + `","status":` + itoa(c.status) + `}`))
		}
		if got := FromResponse(c.status, p); got != c.want {
			t.Errorf("FromResponse(%d, %s) = %d, want %d", c.status, c.code, got, c.want)
		}
	}
}

func itoa(i int) string {
	return string(rune('0'+i/100)) + string(rune('0'+i/10%10)) + string(rune('0'+i%10))
}
