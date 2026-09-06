// Package problem reads the one error document the API has (docs/api.md,
// Errors): RFC 9457 with a code in the last segment of `type`.
package problem

import (
	"encoding/json"
	"strings"
)

// Problem is application/problem+json as ha reads it. Extension members stay
// in Extra so that a command can print what its code carries — the holder on
// stale, the field errors on validation — without this package knowing every code.
type Problem struct {
	Type     string
	Title    string
	Status   int
	Detail   string
	Instance string
	Extra    map[string]json.RawMessage
}

// Parse reads a body as a problem document. A body that is not one — empty,
// or not JSON — is nil: the caller falls back to the status alone.
func Parse(body []byte) *Problem {
	if len(body) == 0 {
		return nil
	}

	var raw map[string]json.RawMessage
	if err := json.Unmarshal(body, &raw); err != nil {
		return nil
	}
	if _, hasType := raw["type"]; !hasType {
		return nil
	}

	p := &Problem{Extra: map[string]json.RawMessage{}}
	for key, value := range raw {
		switch key {
		case "type":
			_ = json.Unmarshal(value, &p.Type)
		case "title":
			_ = json.Unmarshal(value, &p.Title)
		case "status":
			_ = json.Unmarshal(value, &p.Status)
		case "detail":
			_ = json.Unmarshal(value, &p.Detail)
		case "instance":
			_ = json.Unmarshal(value, &p.Instance)
		default:
			p.Extra[key] = value
		}
	}

	return p
}

// Code is the last segment of `type`: `not-found` from `/problems/not-found`.
func (p *Problem) Code() string {
	if p == nil {
		return ""
	}
	if i := strings.LastIndexByte(p.Type, '/'); i >= 0 {
		return p.Type[i+1:]
	}
	return p.Type
}

// Message is what ha prints to stderr: the detail when there is one, the title
// otherwise, and the code so that a person can look it up.
func (p *Problem) Message() string {
	if p == nil {
		return ""
	}
	text := p.Detail
	if text == "" {
		text = p.Title
	}
	if code := p.Code(); code != "" {
		return text + " (" + code + ")"
	}
	return text
}
