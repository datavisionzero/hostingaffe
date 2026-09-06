package version

import "testing"

func TestCompatible(t *testing.T) {
	cases := []struct {
		cli, server string
		ok          bool
	}{
		{"0.0.0-dev", "1.2.0", true},
		{"1.2.0", "0.0.0-dev", true},
		{"1.2.0", "", true},
		{"1.2.0", "1.2.0", true},
		{"1.2.0", "1.1.9", true},
		{"1.2.0", "1.3.0", false},
		{"1.2.0", "2.0.0", false},
		{"2.0.0", "1.9.0", false},
		{"1.2.0-rc.1", "1.2.0+abc", true},
	}
	for _, c := range cases {
		ok, reason := Compatible(c.cli, c.server)
		if ok != c.ok {
			t.Errorf("Compatible(%q, %q) = %v, want %v (%s)", c.cli, c.server, ok, c.ok, reason)
		}
		if !ok && reason == "" {
			t.Errorf("Compatible(%q, %q) refused without a reason", c.cli, c.server)
		}
	}
}
