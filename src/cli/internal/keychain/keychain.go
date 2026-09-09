// Package keychain is where a person's session token lives — the operating
// system's own store, and nowhere else quietly (ADR 0005).
//
// An agent's token never comes through here. It arrives in the environment as
// HOSTINGAFFE_TOKEN, which is how the harness that started the agent hands it
// over, and how a `files sync` on a machine holds one for the length of an SSH
// session and no longer (VISION 9).
package keychain

import (
	"errors"
	"fmt"

	"github.com/zalando/go-keyring"
)

// Service is the name this CLI's entries carry in the store. One entry per
// instance — the account is the address — so two instances on one machine do
// not overwrite each other's session.
const Service = "hostingaffe"

// ErrUnavailable is no keychain on this machine: a headless Linux with no
// Secret Service, most often. It is not a failure to be worked around
// silently — writing a credential to a file nobody asked for is the behaviour
// ADR 0005 exists to not have.
var ErrUnavailable = errors.New("no keychain")

// ErrNotFound is a keychain that works and holds nothing for this instance.
var ErrNotFound = errors.New("no session for this instance")

// System is the machine's own store, as the command tree uses it.
type System struct{}

// Store keeps the token for instance.
func (System) Store(instance, token string) error {
	return translate(keyring.Set(Service, instance, token))
}

// Read answers the token stored for instance.
func (System) Read(instance string) (string, error) {
	token, err := keyring.Get(Service, instance)
	if err != nil {
		return "", translate(err)
	}
	return token, nil
}

// Forget removes the entry for instance. Removing one that is not there is not
// an error: `ha logout` twice is not a failure.
func (System) Forget(instance string) error {
	err := keyring.Delete(Service, instance)
	if err == nil || errors.Is(err, keyring.ErrNotFound) {
		return nil
	}
	return translate(err)
}

// Advice is what ha says when there is no keychain: the two ways to hold a
// token on a machine that has no store of its own, named rather than chosen
// for the person.
const Advice = `This machine has no keychain ha can use (a headless Linux without a Secret Service, most often).
Nothing was written: a token belongs in a store, and writing one to a file you did not ask for is not a decision ha makes for you.
Two ways on:
  • put a token in the environment as HOSTINGAFFE_TOKEN — how an agent receives one anyway, and how CI holds one;
  • or choose a file yourself: ha login --token-file ~/.config/hostingaffe/token, which writes it readable only by you.`

func translate(err error) error {
	switch {
	case err == nil:
		return nil
	case errors.Is(err, keyring.ErrNotFound):
		return ErrNotFound
	case errors.Is(err, keyring.ErrUnsupportedPlatform):
		return ErrUnavailable
	default:
		// go-keyring answers a missing Secret Service as a D-Bus error rather
		// than as a kind of its own, and there is nothing else it means here.
		return fmt.Errorf("%w: %v", ErrUnavailable, err)
	}
}
