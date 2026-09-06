// Package version is what this build of ha calls itself, and the rule for
// whether it talks to a given instance (ADR 0011).
package version

import (
	"fmt"
	"strconv"
	"strings"
)

// Version is the tag this binary was cut from, set by the release build with
// `-ldflags "-X …/internal/version.Version=1.2.3"`. A build nobody tagged says
// so rather than claiming a number that was never released.
var Version = "0.0.0-dev"

// Dev is what an untagged build of either side calls itself.
const Dev = "0.0.0-dev"

// Compatible says whether a CLI of version cli may talk to an instance of
// version server: the same major, and the instance's minor no newer than the
// CLI's own — a CLI talks to installations of its own minor version and older
// (ADR 0011). A development build on either side is not checked: it has no
// number to compare, and the check exists for skew between releases.
func Compatible(cli, server string) (ok bool, reason string) {
	if cli == Dev || server == Dev || server == "" {
		return true, ""
	}

	cMajor, cMinor, cOK := parse(cli)
	sMajor, sMinor, sOK := parse(server)
	if !cOK || !sOK {
		return true, ""
	}

	switch {
	case cMajor != sMajor:
		return false, fmt.Sprintf("ha %s does not talk to a hostingaffe %s: the major versions differ. Install the ha that matches the instance.", cli, server)
	case sMinor > cMinor:
		return false, fmt.Sprintf("ha %s is older than the hostingaffe %s it is talking to. Upgrade ha.", cli, server)
	default:
		return true, ""
	}
}

func parse(v string) (major, minor int, ok bool) {
	v = strings.TrimPrefix(v, "v")
	if plus := strings.IndexByte(v, '+'); plus >= 0 {
		v = v[:plus]
	}
	if dash := strings.IndexByte(v, '-'); dash >= 0 {
		v = v[:dash]
	}

	parts := strings.Split(v, ".")
	if len(parts) < 2 {
		return 0, 0, false
	}

	var err error
	if major, err = strconv.Atoi(parts[0]); err != nil {
		return 0, 0, false
	}
	if minor, err = strconv.Atoi(parts[1]); err != nil {
		return 0, 0, false
	}

	return major, minor, true
}
