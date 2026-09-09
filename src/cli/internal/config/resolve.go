package config

import (
	"fmt"
	"strings"
)

// Resolved is what a command runs with, and where the token came from. The
// provenance is not decoration: `ha status` prints it, and "as whom am I about
// to write this" is the question somebody asks a second before they would have
// been sorry.
type Resolved struct {
	Address   string
	Token     string
	TokenFrom string
}

// Input is everything Resolve reads, so that a test supplies all of it and
// nothing reaches around it to the real machine.
type Input struct {
	Getenv         func(string) string
	File           File
	Address        string
	AllowPlainHTTP bool
	ReadKeychain   func(instance string) (string, error)
}

// ResolveAddress answers which instance this invocation talks to: the flag,
// then the environment, then the instance this machine signed in to.
func (in Input) ResolveAddress() (string, error) {
	address := strings.TrimSpace(in.Address)
	if address == "" {
		address = strings.TrimSpace(in.getenv(EnvURL))
	}
	if address == "" {
		address = strings.TrimSpace(in.File.Instance)
	}
	if address == "" {
		return "", &UsageError{fmt.Sprintf(
			"no instance: run `ha login --url https://hosting.example.com`, or set %s.", EnvURL)}
	}

	address = strings.TrimRight(address, "/")
	if err := CheckAddress(address, in.AllowsPlainHTTP()); err != nil {
		return "", err
	}

	return address, nil
}

// ResolveToken answers the token and where it came from. The environment wins,
// because that is how an agent receives its own token and how CI holds one; a
// file the person chose is next, because they chose it; the keychain is last
// and is where `ha login` puts a session unless it was told otherwise.
func (in Input) ResolveToken(address string) (string, string, error) {
	if token := strings.TrimSpace(in.getenv(EnvToken)); token != "" {
		return token, EnvToken, nil
	}

	if path := strings.TrimSpace(in.File.TokenFile); path != "" {
		token, err := ReadTokenFile(path)
		if err != nil {
			return "", "", err
		}
		return token, path, nil
	}

	if in.ReadKeychain != nil {
		if token, err := in.ReadKeychain(address); err == nil && strings.TrimSpace(token) != "" {
			return strings.TrimSpace(token), "the keychain", nil
		}
	}

	return "", "", &UsageError{fmt.Sprintf(
		"no token for %s: run `ha login`, or put a user token or an agent token in %s.", address, EnvToken)}
}

// Resolve is both at once: what every command that talks to the instance needs.
func Resolve(in Input) (Resolved, error) {
	address, err := in.ResolveAddress()
	if err != nil {
		return Resolved{}, err
	}

	token, from, err := in.ResolveToken(address)
	if err != nil {
		return Resolved{Address: address}, err
	}

	return Resolved{Address: address, Token: token, TokenFrom: from}, nil
}

// AllowsPlainHTTP is the flag or the variable, and nothing inferred.
func (in Input) AllowsPlainHTTP() bool {
	if in.AllowPlainHTTP {
		return true
	}

	switch strings.TrimSpace(in.getenv(EnvInsecureHTTP)) {
	case "1", "true", "yes":
		return true
	default:
		return false
	}
}

func (in Input) getenv(name string) string {
	if in.Getenv == nil {
		return ""
	}
	return in.Getenv(name)
}
