// Package collect is what `ha report collect` gathers on a host: a sign of
// life, plus the handful of things that can be determined on any Linux machine
// in a standard way (CONTEXT.md, Report).
//
// Three rules hold everything here together.
//
// **Nothing is installed and nothing is assumed** beyond what lies on every
// Linux host: /proc, /etc/os-release, `uname`, `df`, and `docker` where there
// is one. Where /proc can answer, /proc is read rather than a command run.
//
// **A missing section is not a failure.** A host without Docker reports no
// containers and says why; the sign of life is the point, and a cron that
// failed over a section nobody could determine would be switched off within a
// fortnight.
//
// **No secrets, ever.** No container environment, no process command lines, no
// file contents, no labels of arbitrary content. That is not a convenience
// rule: it is the line that keeps secret values out of the record, and the
// reason `collect` prints what `send` would send, so that anyone can check it
// without reading this file.
package collect

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
)

// What the instance takes at most, held here too so that a collector never
// builds a body the door would refuse (docs/api.md, Reports).
const (
	MaxDisks      = 64
	MaxContainers = 500
	MaxPorts      = 64
	MaxMissing    = 8
)

// CommandTimeout is what any one command gets. A hanging `docker` must not hold
// the run: the next quarter of an hour is not far away, and a report that never
// finishes is worse than one with a section missing.
const CommandTimeout = 5 * time.Second

// Report is what leaves the host — the body of docs/api.md, Reports, and
// nothing else. A nil section is a section that is not there; an empty slice is
// a section that is there and empty, which is a different thing: a host with
// Docker and no containers says `[]`, and a host without Docker says nothing
// and names itself in Missing.
type Report struct {
	CollectedAt time.Time   `json:"collected_at"`
	Agent       string      `json:"agent,omitempty"`
	Host        *Host       `json:"host"`
	Memory      *Memory     `json:"memory"`
	Disks       []Disk      `json:"disks"`
	Containers  []Container `json:"containers"`
	Missing     []Missing   `json:"missing"`
}

// Host is the machine as it describes itself.
type Host struct {
	Hostname string   `json:"hostname,omitempty"`
	Os       string   `json:"os,omitempty"`
	Kernel   string   `json:"kernel,omitempty"`
	Arch     string   `json:"arch,omitempty"`
	Uptime   *int64   `json:"uptime_seconds,omitempty"`
	Load1    *float64 `json:"load1,omitempty"`
	Load5    *float64 `json:"load5,omitempty"`
	Load15   *float64 `json:"load15,omitempty"`
}

// Memory is bytes, never a formatted size: `42G` is a rendering, and the
// instance stores numbers.
type Memory struct {
	Total     *int64 `json:"total_bytes,omitempty"`
	Used      *int64 `json:"used_bytes,omitempty"`
	Available *int64 `json:"available_bytes,omitempty"`
	SwapTotal *int64 `json:"swap_total_bytes,omitempty"`
	SwapUsed  *int64 `json:"swap_used_bytes,omitempty"`
}

// Disk is one real mount.
type Disk struct {
	Mount   string `json:"mount"`
	Device  string `json:"device,omitempty"`
	Size    *int64 `json:"size_bytes,omitempty"`
	Used    *int64 `json:"used_bytes,omitempty"`
	Percent *int   `json:"percent,omitempty"`
}

// Container is one container, with its image's tag — which is the whole point
// of the comparison the record makes against it.
type Container struct {
	Name      string     `json:"name"`
	Image     string     `json:"image,omitempty"`
	State     string     `json:"state,omitempty"`
	Status    string     `json:"status,omitempty"`
	Health    string     `json:"health,omitempty"`
	Restarts  *int       `json:"restarts,omitempty"`
	StartedAt *time.Time `json:"started_at,omitempty"`
	Ports     []string   `json:"ports,omitempty"`
}

// Missing is a section the collector could not determine, and the reason it
// gives for it.
type Missing struct {
	Section string `json:"section"`
	Reason  string `json:"reason"`
}

// Environment is the host as the collector reaches it, so that a test supplies
// all of it and nothing reaches around to the machine running the tests.
type Environment struct {
	// Root is where /proc and /etc are found; empty is the machine's own.
	Root string
	// Run executes one command with a timeout of its own.
	Run func(ctx context.Context, name string, args ...string) ([]byte, error)
	// Now is the host's clock. The instance trusts it for nothing and records
	// its own time of arrival (ADR 0015).
	Now func() time.Time
}

// Machine is the environment of the host this runs on.
func Machine() Environment {
	return Environment{Root: "/", Run: run, Now: time.Now}
}

func run(ctx context.Context, name string, args ...string) ([]byte, error) {
	ctx, cancel := context.WithTimeout(ctx, CommandTimeout)
	defer cancel()

	out, err := exec.CommandContext(ctx, name, args...).Output()
	if err != nil {
		return out, err
	}
	return out, nil
}

// Collect gathers everything it can and says what it could not. It never fails:
// what is not there is named in Missing, and the report goes out regardless.
func Collect(ctx context.Context, env Environment, agent string) Report {
	report := Report{CollectedAt: env.now().UTC(), Agent: agent, Missing: []Missing{}}

	if host, err := collectHost(ctx, env); err != nil {
		report.missing("host", err)
	} else {
		report.Host = host
	}

	if memory, err := collectMemory(env); err != nil {
		report.missing("memory", err)
	} else {
		report.Memory = memory
	}

	if disks, err := collectDisks(ctx, env); err != nil {
		report.missing("disks", err)
	} else {
		report.Disks = disks
	}

	if containers, err := collectContainers(ctx, env); err != nil {
		report.missing("containers", err)
	} else {
		report.Containers = containers
	}

	return report
}

func (r *Report) missing(section string, err error) {
	if len(r.Missing) >= MaxMissing {
		return
	}
	r.Missing = append(r.Missing, Missing{Section: section, Reason: reason(err)})
}

// reason is one line a person can act on, and never a stack of wrapped errors.
func reason(err error) string {
	said := strings.TrimSpace(strings.ReplaceAll(err.Error(), "\n", " "))
	if len(said) > 300 {
		said = said[:300]
	}
	return said
}

func (e Environment) now() time.Time {
	if e.Now == nil {
		return time.Now()
	}
	return e.Now()
}

func (e Environment) path(parts ...string) string {
	root := e.Root
	if root == "" {
		root = "/"
	}
	return filepath.Join(append([]string{root}, parts...)...)
}

func (e Environment) read(parts ...string) (string, error) {
	content, err := os.ReadFile(e.path(parts...))
	if err != nil {
		return "", err
	}
	return string(content), nil
}

func (e Environment) run(ctx context.Context, name string, args ...string) ([]byte, error) {
	if e.Run == nil {
		return run(ctx, name, args...)
	}
	return e.Run(ctx, name, args...)
}

func collectHost(ctx context.Context, env Environment) (*Host, error) {
	host := &Host{}

	if name, err := env.read("proc", "sys", "kernel", "hostname"); err == nil {
		host.Hostname = strings.TrimSpace(name)
	}
	if kernel, err := env.read("proc", "sys", "kernel", "osrelease"); err == nil {
		host.Kernel = strings.TrimSpace(kernel)
	}
	if release, err := env.read("etc", "os-release"); err == nil {
		host.Os = prettyName(release)
	}
	if arch, err := env.run(ctx, "uname", "-m"); err == nil {
		host.Arch = strings.TrimSpace(string(arch))
	}

	if uptime, err := env.read("proc", "uptime"); err == nil {
		if fields := strings.Fields(uptime); len(fields) > 0 {
			if seconds, err := strconv.ParseFloat(fields[0], 64); err == nil && seconds >= 0 {
				whole := int64(seconds)
				host.Uptime = &whole
			}
		}
	}

	if load, err := env.read("proc", "loadavg"); err == nil {
		fields := strings.Fields(load)
		into := []**float64{&host.Load1, &host.Load5, &host.Load15}
		for i := 0; i < 3 && i < len(fields); i++ {
			if value, err := strconv.ParseFloat(fields[i], 64); err == nil && value >= 0 {
				*into[i] = &value
			}
		}
	}

	if *host == (Host{}) {
		return nil, fmt.Errorf("%s could not be read", env.path("proc"))
	}
	return host, nil
}

// prettyName is what /etc/os-release calls the distribution, unquoted. Where
// there is none, NAME does, and where there is neither, nothing does.
func prettyName(release string) string {
	values := map[string]string{}
	scanner := bufio.NewScanner(strings.NewReader(release))
	for scanner.Scan() {
		key, value, found := strings.Cut(scanner.Text(), "=")
		if !found {
			continue
		}
		values[strings.TrimSpace(key)] = strings.Trim(strings.TrimSpace(value), `"`)
	}

	if pretty := values["PRETTY_NAME"]; pretty != "" {
		return pretty
	}
	return values["NAME"]
}

func collectMemory(env Environment) (*Memory, error) {
	content, err := env.read("proc", "meminfo")
	if err != nil {
		return nil, err
	}

	// /proc/meminfo counts in kibibytes; the report counts in bytes.
	values := map[string]int64{}
	scanner := bufio.NewScanner(strings.NewReader(content))
	for scanner.Scan() {
		key, rest, found := strings.Cut(scanner.Text(), ":")
		if !found {
			continue
		}
		fields := strings.Fields(rest)
		if len(fields) == 0 {
			continue
		}
		if number, err := strconv.ParseInt(fields[0], 10, 64); err == nil {
			values[key] = number * 1024
		}
	}

	if len(values) == 0 {
		return nil, fmt.Errorf("%s says nothing this understands", env.path("proc", "meminfo"))
	}

	memory := &Memory{}
	total, hasTotal := values["MemTotal"]
	available, hasAvailable := values["MemAvailable"]
	if hasTotal {
		memory.Total = &total
	}
	if hasAvailable {
		memory.Available = &available
	}
	if hasTotal && hasAvailable && total >= available {
		used := total - available
		memory.Used = &used
	}

	swapTotal, hasSwapTotal := values["SwapTotal"]
	swapFree, hasSwapFree := values["SwapFree"]
	if hasSwapTotal {
		memory.SwapTotal = &swapTotal
	}
	if hasSwapTotal && hasSwapFree && swapTotal >= swapFree {
		used := swapTotal - swapFree
		memory.SwapUsed = &used
	}

	return memory, nil
}

// pseudo are the filesystems a person does not mean when they ask how full the
// machine is: the ones that live in memory, and the layers a container image is
// made of.
var pseudo = map[string]bool{
	"tmpfs": true, "devtmpfs": true, "overlay": true, "squashfs": true,
	"proc": true, "sysfs": true, "cgroup": true, "cgroup2": true, "devpts": true,
	"mqueue": true, "hugetlbfs": true, "debugfs": true, "tracefs": true,
	"securityfs": true, "pstore": true, "bpf": true, "configfs": true,
	"fusectl": true, "binfmt_misc": true, "autofs": true, "ramfs": true,
	"nsfs": true, "efivarfs": true, "fuse.snapfuse": true, "fuse.gvfsd-fuse": true,
}

// collectDisks asks /proc/mounts which mounts are real and `df` how full each
// one is: /proc says what a filesystem is, and df is the one portable way to
// ask how much of it is used.
func collectDisks(ctx context.Context, env Environment) ([]Disk, error) {
	mounts, err := realMounts(env)
	if err != nil {
		return nil, err
	}

	out, err := env.run(ctx, "df", "-P", "-B1")
	if err != nil {
		return nil, fmt.Errorf("df: %w", err)
	}

	disks := []Disk{}
	seen := map[string]bool{}
	scanner := bufio.NewScanner(strings.NewReader(string(out)))
	for scanner.Scan() {
		fields := strings.Fields(scanner.Text())
		if len(fields) < 6 || fields[0] == "Filesystem" {
			continue
		}

		// `Mounted on` is the last field and may carry spaces; df -P puts it
		// last and nothing after it.
		mount := strings.Join(fields[5:], " ")
		if !mounts[mount] || seen[mount] {
			continue
		}
		seen[mount] = true

		disk := Disk{Mount: mount, Device: fields[0]}
		if size, err := strconv.ParseInt(fields[1], 10, 64); err == nil {
			disk.Size = &size
		}
		if used, err := strconv.ParseInt(fields[2], 10, 64); err == nil {
			disk.Used = &used
		}
		if percent, err := strconv.Atoi(strings.TrimSuffix(fields[4], "%")); err == nil && percent >= 0 && percent <= 100 {
			disk.Percent = &percent
		}

		disks = append(disks, disk)
		if len(disks) == MaxDisks {
			break
		}
	}

	return disks, nil
}

// realMounts is every mount point whose filesystem is one a person means when
// they ask how full the machine is.
func realMounts(env Environment) (map[string]bool, error) {
	content, err := env.read("proc", "mounts")
	if err != nil {
		return nil, err
	}

	mounts := map[string]bool{}
	scanner := bufio.NewScanner(strings.NewReader(content))
	for scanner.Scan() {
		fields := strings.Fields(scanner.Text())
		if len(fields) < 3 || pseudo[fields[2]] {
			continue
		}
		// /proc/mounts escapes a space in a path as \040.
		mounts[strings.ReplaceAll(fields[1], `\040`, " ")] = true
	}

	return mounts, nil
}

// dockerLine is the part of `docker ps --format json` this reads, and nothing
// else. It is an allowlist rather than a filter: what is not named here never
// enters the process, let alone the report.
type dockerLine struct {
	ID     string `json:"ID"`
	Names  string `json:"Names"`
	Image  string `json:"Image"`
	State  string `json:"State"`
	Status string `json:"Status"`
	Ports  string `json:"Ports"`
}

// collectContainers asks `docker ps` what is there and `docker inspect` the two
// things `ps` does not say. The inspect template names the four fields it wants
// and nothing else, which is what makes "no environment, no command lines" a
// thing somebody can check rather than a promise.
func collectContainers(ctx context.Context, env Environment) ([]Container, error) {
	out, err := env.run(ctx, "docker", "ps", "--all", "--no-trunc", "--format", "{{json .}}")
	if err != nil {
		return nil, dockerTrouble(err)
	}

	containers := []Container{}
	ids := []string{}
	scanner := bufio.NewScanner(strings.NewReader(string(out)))
	scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024)
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" {
			continue
		}

		var said dockerLine
		if err := json.Unmarshal([]byte(line), &said); err != nil {
			continue
		}

		// A container has one name here; `docker ps` joins several with commas
		// where there are several, and the first is the one a person uses.
		name, _, _ := strings.Cut(said.Names, ",")
		container := Container{
			Name:   strings.TrimSpace(name),
			Image:  strings.TrimSpace(said.Image),
			State:  strings.TrimSpace(said.State),
			Status: strings.TrimSpace(said.Status),
			Ports:  ports(said.Ports),
		}
		if container.Name == "" {
			continue
		}

		containers = append(containers, container)
		ids = append(ids, said.ID)
		if len(containers) == MaxContainers {
			break
		}
	}

	if len(containers) > 0 {
		inspected(ctx, env, ids, containers)
	}
	return containers, nil
}

// inspected fills in what `docker ps` does not say. A failure here is not a
// failure of the section: the containers are known, and how often one restarted
// is worth less than saying it is there at all.
func inspected(ctx context.Context, env Environment, ids []string, containers []Container) {
	const template = "{{.RestartCount}}\t{{.State.StartedAt}}\t{{if .State.Health}}{{.State.Health.Status}}{{end}}"

	out, err := env.run(ctx, "docker", append([]string{"inspect", "--format", template}, ids...)...)
	if err != nil {
		return
	}

	lines := strings.Split(strings.TrimRight(string(out), "\n"), "\n")
	for i := range containers {
		if i >= len(lines) {
			return
		}

		fields := strings.SplitN(lines[i], "\t", 3)
		if len(fields) < 3 {
			continue
		}
		if restarts, err := strconv.Atoi(strings.TrimSpace(fields[0])); err == nil && restarts >= 0 {
			containers[i].Restarts = &restarts
		}
		if started, err := time.Parse(time.RFC3339Nano, strings.TrimSpace(fields[1])); err == nil && !started.IsZero() {
			at := started.UTC()
			containers[i].StartedAt = &at
		}
		containers[i].Health = strings.TrimSpace(fields[2])
	}
}

// ports is what `docker ps` prints in its Ports column, as a list. It is text
// the host wrote and is passed through as it stands.
func ports(said string) []string {
	said = strings.TrimSpace(said)
	if said == "" {
		return nil
	}

	list := []string{}
	for _, port := range strings.Split(said, ",") {
		if port = strings.TrimSpace(port); port != "" {
			list = append(list, port)
		}
		if len(list) == MaxPorts {
			break
		}
	}
	return list
}

// dockerTrouble says which of the two ordinary troubles it was, because they
// need different things of a person: one installs Docker, the other joins a
// group.
func dockerTrouble(err error) error {
	var notFound *exec.Error
	if errors.As(err, &notFound) {
		return errors.New("docker is not installed, or not on the PATH")
	}

	var failed *exec.ExitError
	said := err.Error()
	if errors.As(err, &failed) && len(failed.Stderr) > 0 {
		said = strings.TrimSpace(strings.ReplaceAll(string(failed.Stderr), "\n", " "))
	}
	if strings.Contains(said, "permission denied") {
		return fmt.Errorf("the docker socket is not readable by this user; add it to the docker group")
	}
	return fmt.Errorf("docker: %s", said)
}

// Body is the report as the contract's object, so that `send` hands the
// instance exactly what `collect` printed and the two cannot drift apart.
func (r Report) Body() api.HandInReportRequest {
	body := api.HandInReportRequest{CollectedAt: &r.CollectedAt}
	if r.Agent != "" {
		body.Agent = &r.Agent
	}

	if r.Host != nil {
		body.Host = &api.HostSectionRequest{
			Hostname:      text(r.Host.Hostname),
			Os:            text(r.Host.Os),
			Kernel:        text(r.Host.Kernel),
			Arch:          text(r.Host.Arch),
			UptimeSeconds: r.Host.Uptime,
			Load1:         r.Host.Load1,
			Load5:         r.Host.Load5,
			Load15:        r.Host.Load15,
		}
	}

	if r.Memory != nil {
		body.Memory = &api.MemorySectionRequest{
			TotalBytes:     r.Memory.Total,
			UsedBytes:      r.Memory.Used,
			AvailableBytes: r.Memory.Available,
			SwapTotalBytes: r.Memory.SwapTotal,
			SwapUsedBytes:  r.Memory.SwapUsed,
		}
	}

	if r.Disks != nil {
		disks := make([]api.DiskRequest, 0, len(r.Disks))
		for _, disk := range r.Disks {
			disks = append(disks, api.DiskRequest{
				Mount:     text(disk.Mount),
				Device:    text(disk.Device),
				SizeBytes: disk.Size,
				UsedBytes: disk.Used,
				Percent:   small(disk.Percent),
			})
		}
		body.Disks = &disks
	}

	if r.Containers != nil {
		containers := make([]api.ContainerRequest, 0, len(r.Containers))
		for _, container := range r.Containers {
			one := api.ContainerRequest{
				Name:      text(container.Name),
				Image:     text(container.Image),
				State:     text(container.State),
				Status:    text(container.Status),
				Health:    text(container.Health),
				Restarts:  small(container.Restarts),
				StartedAt: container.StartedAt,
			}
			if container.Ports != nil {
				ports := container.Ports
				one.Ports = &ports
			}
			containers = append(containers, one)
		}
		body.Containers = &containers
	}

	missing := make([]api.MissingRequest, 0, len(r.Missing))
	for _, one := range r.Missing {
		missing = append(missing, api.MissingRequest{Section: text(one.Section), Reason: text(one.Reason)})
	}
	body.Missing = &missing

	return body
}

// text is a value the contract leaves out where there is nothing to say.
func text(value string) *string {
	if value == "" {
		return nil
	}
	return &value
}

func small(value *int) *int32 {
	if value == nil {
		return nil
	}
	narrowed := int32(*value)
	return &narrowed
}
