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
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"os/exec"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/datavisionzero/hostingaffe/src/cli/internal/api"
	"github.com/datavisionzero/hostingaffe/src/cli/internal/manifest"
)

// What the instance takes at most, held here too so that a collector never
// builds a body the door would refuse (docs/api.md, Reports).
const (
	MaxDisks      = 64
	MaxContainers = 500
	MaxPorts      = 64
	MaxListening  = 128
	MaxMissing    = 8

	MaxSyncedDirectories = 32
	MaxSyncedFiles       = 128
)

// MaxHashedBytes is how much of a file this will read to hash it. A file of the
// record is a megabyte at most, so anything past this is not what the record
// has whatever its digest turns out to be — and reading it would be a cron
// stalling on whatever somebody put at that path.
const MaxHashedBytes = 16 << 20

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
	Listening   []Listening `json:"listening"`
	Updates     *Updates    `json:"updates"`
	Files       []Synced    `json:"files"`
	Missing     []Missing   `json:"missing"`
}

// Synced is one directory `ha files sync` wrote an installation's files into,
// and the digest of what lies at each of those paths now.
//
// **The manifest is the whole of what is reported.** A file sync never wrote is
// not in it and is not named here — the same rule sync itself keeps, and what
// keeps the names of whatever else lies in a compose directory off the wire.
// Every path here came out of the record in the first place.
//
// **A digest and never a content.** That is what lets a machine say whether its
// configuration still matches the record without handing the configuration
// over, and it is why this needs no token that reads (ADR 0016, ADR 0017).
type Synced struct {
	Installation string       `json:"installation"`
	Directory    string       `json:"directory"`
	Files        []SyncedFile `json:"files"`
}

// SyncedFile is one path the manifest claims, and what lies there now.
type SyncedFile struct {
	Path string `json:"path"`
	// Sha256 is nothing where nothing lies at the path any more, which is a
	// finding rather than an omission.
	Sha256 *string `json:"sha256"`
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

// Listening is one port the machine listens on, and how far the socket is
// bound — and it is deliberately nothing else.
//
// **No process name, no command line, no arguments.** `ss -tulpn` shows another
// user's process only as root, and this collector's promise is that it needs
// none; a section whole on the machine whose cron runs as root and half empty
// on the next would be worse than one that says the same everywhere. What the
// section exists for is the comparison against an installation's ports, and
// those are ports rather than processes.
type Listening struct {
	Port     int    `json:"port"`
	Protocol string `json:"protocol"`
	Binding  string `json:"binding"`
}

// Updates is what the machine says about its own upkeep. Today one thing:
// whether it is waiting for a restart.
//
// How many packages have an update is deliberately not here. Counting them
// makes this distribution-dependent for the first time — apt, dnf, apk, pacman,
// each with its own command — and a host on which nothing ran `apt update` for
// weeks would report nothing pending and lie in the most comforting way there
// is. Where the restart cannot be told, the section is missing and says so; a
// false from a machine nobody could ask would be the worst of the three
// answers.
type Updates struct {
	RebootRequired bool `json:"reboot_required"`
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

// It never fails: what is not there is named in Missing, and the report goes
// out regardless.
// Collect gathers everything it can and says what it could not. Where dirs are
// given, each is a directory `files sync` wrote into, and what lies there is
// hashed and reported beside the rest; where none are, the section is absent
// and nothing about files is compared (ADR 0017).
func Collect(ctx context.Context, env Environment, agent string, dirs ...string) Report {
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

	if listening, err := collectListening(env); err != nil {
		report.missing("listening", err)
	} else {
		report.Listening = listening
	}

	if updates, err := collectUpdates(env); err != nil {
		report.missing("updates", err)
	} else {
		report.Updates = updates
	}

	// A directory that could not be read is said once, beside the ones that
	// could: the section carries what was determined and `missing` names what
	// was not, so that a finding absent for want of a reading is not read as a
	// directory in order.
	if len(dirs) > 0 {
		synced, complaints := collectFiles(dirs)
		if synced != nil {
			report.Files = synced
		}
		if len(complaints) > 0 {
			report.missing("files", errors.New(strings.Join(complaints, "; ")))
		}
	}

	return report
}

// collectFiles reads the manifest beside each directory and hashes what lies at
// the paths it claims. Nothing else in the directory is looked at, let alone
// named.
func collectFiles(dirs []string) ([]Synced, []string) {
	var synced []Synced
	var complaints []string
	held := map[string]string{}

	for _, dir := range dirs {
		if len(synced) >= MaxSyncedDirectories {
			complaints = append(complaints, fmt.Sprintf(
				"%s and the ones after it were left out: a report carries %d directories at most", dir, MaxSyncedDirectories))
			break
		}

		manifested, err := manifest.Read(dir)
		if err != nil {
			complaints = append(complaints, fmt.Sprintf("%s: %v", dir, err))
			continue
		}
		if manifested.Owner == "" {
			complaints = append(complaints, fmt.Sprintf("%s: sync has never written there", dir))
			continue
		}

		key, ok := manifested.InstallationKey()
		if !ok {
			complaints = append(complaints, fmt.Sprintf("%s: %s is not an installation's directory", dir, manifested.Owner))
			continue
		}

		// An installation has one directory in the record, so a report claiming
		// two for it says two things that cannot both be answered — and the
		// instance refuses the whole body for it. It is said here instead, so
		// that a cron given the same directory twice keeps reporting.
		if first, already := held[key]; already {
			complaints = append(complaints, fmt.Sprintf(
				"%s was left out: %s already holds the files of %s", dir, first, key))
			continue
		}
		held[key] = dir

		one := Synced{Installation: key, Directory: dir, Files: []SyncedFile{}}

		paths := make([]string, 0, len(manifested.Files))
		for path := range manifested.Files {
			paths = append(paths, path)
		}
		sort.Strings(paths)

		for _, path := range paths {
			if len(one.Files) >= MaxSyncedFiles {
				complaints = append(complaints, fmt.Sprintf(
					"%s carries more than the %d files a report holds", dir, MaxSyncedFiles))
				break
			}

			digest, err := hashAt(dir, path)
			if err != nil {
				complaints = append(complaints, fmt.Sprintf("%s/%s: %v", dir, path, err))
			}
			one.Files = append(one.Files, SyncedFile{Path: path, Sha256: digest})
		}

		synced = append(synced, one)
	}

	if len(complaints) > MaxMissing {
		complaints = complaints[:MaxMissing]
	}
	return synced, complaints
}

// hashAt is the digest of what lies at the path, or nothing where nothing does.
// Anything that is not a plain file is nothing: a symlink, a socket or a device
// is not what sync wrote there, and opening one is how a cron stops coming back.
func hashAt(dir, path string) (*string, error) {
	at := filepath.Join(dir, filepath.FromSlash(path))

	info, err := os.Lstat(at)
	if os.IsNotExist(err) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	if !info.Mode().IsRegular() {
		return nil, fmt.Errorf("what lies there is not a plain file")
	}
	if info.Size() > MaxHashedBytes {
		return nil, fmt.Errorf("it is larger than the %d bytes this reads", int64(MaxHashedBytes))
	}

	file, err := os.Open(at)
	if err != nil {
		return nil, err
	}
	defer file.Close()

	sum := sha256.New()
	if _, err := io.CopyN(sum, file, MaxHashedBytes+1); err != nil && !errors.Is(err, io.EOF) {
		return nil, err
	}

	digest := hex.EncodeToString(sum.Sum(nil))
	return &digest, nil
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

// sockets are the four files the kernel already keeps, one per family and
// transport. They are read rather than `ss` run for the reason /proc is read
// everywhere else here: it needs no package, no PATH and no root.
var sockets = []struct {
	parts    []string
	protocol string
}{
	{[]string{"proc", "net", "tcp"}, "tcp"},
	{[]string{"proc", "net", "tcp6"}, "tcp"},
	{[]string{"proc", "net", "udp"}, "udp"},
	{[]string{"proc", "net", "udp6"}, "udp"},
}

// tcpListen is what /proc/net/tcp calls a socket waiting for a connection.
const tcpListen = "0A"

// collectListening is what has a socket open for it on this machine: one entry
// per port and protocol, the widest binding winning where a port is bound to
// several addresses. A port on 0.0.0.0 and on 127.0.0.1 is public, because that
// is the honest answer to how far it is reachable.
func collectListening(env Environment) ([]Listening, error) {
	widest := map[Listening]bool{}
	read := 0

	for _, family := range sockets {
		content, err := env.read(family.parts...)
		if err != nil {
			continue
		}
		read++

		for _, one := range listeners(content, family.protocol) {
			widest[one] = true
		}
	}

	if read == 0 {
		return nil, fmt.Errorf("%s could not be read", env.path("proc", "net"))
	}

	// A port heard in public and on loopback is one port, and it is public.
	public := map[int]map[string]bool{}
	for one := range widest {
		if one.Binding != "public" {
			continue
		}
		if public[one.Port] == nil {
			public[one.Port] = map[string]bool{}
		}
		public[one.Port][one.Protocol] = true
	}

	listening := []Listening{}
	for one := range widest {
		if one.Binding == "loopback" && public[one.Port][one.Protocol] {
			continue
		}
		listening = append(listening, one)
	}

	// Ordered, so that two reports of an unchanged machine are the same text
	// and a person comparing them sees only what moved.
	sort.Slice(listening, func(i, j int) bool {
		if listening[i].Port != listening[j].Port {
			return listening[i].Port < listening[j].Port
		}
		return listening[i].Protocol < listening[j].Protocol
	})

	if len(listening) > MaxListening {
		listening = listening[:MaxListening]
	}
	return listening, nil
}

// listeners is one of the four files, as the entries a person would call
// listening: every TCP socket in LISTEN, and every UDP socket with no peer.
func listeners(content, protocol string) []Listening {
	found := []Listening{}

	scanner := bufio.NewScanner(strings.NewReader(content))
	for scanner.Scan() {
		fields := strings.Fields(scanner.Text())
		if len(fields) < 4 || fields[0] == "sl" {
			continue
		}

		if protocol == "tcp" {
			if fields[3] != tcpListen {
				continue
			}
		} else if _, peer, cut := strings.Cut(fields[2], ":"); !cut || !allZero(peer) {
			// A UDP socket with a peer is a conversation, not a door.
			continue
		}

		address, port, cut := strings.Cut(fields[1], ":")
		if !cut {
			continue
		}
		number, err := strconv.ParseUint(port, 16, 32)
		if err != nil || number < 1 || number > 65535 {
			continue
		}

		binding, err := binding(address)
		if err != nil {
			continue
		}

		found = append(found, Listening{Port: int(number), Protocol: protocol, Binding: binding})
	}

	return found
}

func allZero(hex string) bool {
	return strings.Trim(hex, "0") == ""
}

// binding is how far a socket bound to this address reaches. The kernel writes
// an address as hex words in the host's byte order, which on every platform
// this runs on is little-endian: the bytes of each four come back reversed.
func binding(address string) (string, error) {
	if len(address)%8 != 0 || len(address) == 0 {
		return "", fmt.Errorf("%q is not an address", address)
	}

	raw := make([]byte, 0, len(address)/2)
	for word := 0; word < len(address); word += 8 {
		for pair := 6; pair >= 0; pair -= 2 {
			value, err := strconv.ParseUint(address[word+pair:word+pair+2], 16, 8)
			if err != nil {
				return "", err
			}
			raw = append(raw, byte(value))
		}
	}

	ip := net.IP(raw)
	if len(ip) != net.IPv4len && len(ip) != net.IPv6len {
		return "", fmt.Errorf("%q is not an address", address)
	}

	// The wildcard is the widest bind there is, and net calls it unspecified
	// rather than loopback; it is asked first for that reason.
	if ip.IsUnspecified() || !ip.IsLoopback() {
		return "public", nil
	}
	return "loopback", nil
}

// rebootMarkers are where a distribution says a restart is pending. Debian and
// Ubuntu write the file; /var/run is /run on any system of this age, and both
// are looked at so that a fake root in a test needs no symlink.
var rebootMarkers = [][]string{
	{"run", "reboot-required"},
	{"var", "run", "reboot-required"},
}

// collectUpdates is whether the machine is waiting for a restart.
//
// The marker is Debian's and Ubuntu's, and the section is missing on anything
// else rather than false: this collector runs no package manager, and a `false`
// it could not check would be read as "nothing to do here".
func collectUpdates(env Environment) (*Updates, error) {
	for _, marker := range rebootMarkers {
		if _, err := os.Stat(env.path(marker...)); err == nil {
			return &Updates{RebootRequired: true}, nil
		}
	}

	release, err := env.read("etc", "os-release")
	if err != nil {
		return nil, fmt.Errorf("%s could not be read", env.path("etc", "os-release"))
	}

	if !debianLike(release) {
		return nil, errors.New(
			"this distribution has no reboot-required marker that ha knows about; only Debian and Ubuntu do")
	}

	// On Debian and Ubuntu the marker is written by update-notifier-common. A
	// host without that package never gets one, and this then says no restart
	// is pending when one may be — the one thing the section cannot tell apart,
	// and docs/operations.md says so.
	return &Updates{RebootRequired: false}, nil
}

// debianLike is whether /etc/os-release names a distribution that writes the
// reboot-required marker — either because it is Debian or Ubuntu, or because it
// says it is like one.
func debianLike(release string) bool {
	values := map[string]string{}
	scanner := bufio.NewScanner(strings.NewReader(release))
	for scanner.Scan() {
		key, value, found := strings.Cut(scanner.Text(), "=")
		if !found {
			continue
		}
		values[strings.TrimSpace(key)] = strings.Trim(strings.TrimSpace(value), `"`)
	}

	for _, word := range append(strings.Fields(values["ID_LIKE"]), values["ID"]) {
		if word == "debian" || word == "ubuntu" {
			return true
		}
	}
	return false
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

	if r.Listening != nil {
		listening := make([]api.ListeningRequest, 0, len(r.Listening))
		for _, one := range r.Listening {
			port := int32(one.Port)
			protocol := api.Protocol(one.Protocol)
			binding := api.Binding(one.Binding)
			listening = append(listening, api.ListeningRequest{
				Port:     &port,
				Protocol: &protocol,
				Binding:  &binding,
			})
		}
		body.Listening = &listening
	}

	if r.Updates != nil {
		reboot := r.Updates.RebootRequired
		body.Updates = &api.UpdatesRequest{RebootRequired: &reboot}
	}

	if r.Files != nil {
		synced := make([]api.SyncedDirectoryRequest, 0, len(r.Files))
		for _, one := range r.Files {
			files := make([]api.SyncedFileRequest, 0, len(one.Files))
			for _, file := range one.Files {
				files = append(files, api.SyncedFileRequest{Path: text(file.Path), Sha256: file.Sha256})
			}
			synced = append(synced, api.SyncedDirectoryRequest{
				Installation: text(one.Installation),
				Directory:    text(one.Directory),
				Files:        &files,
			})
		}
		body.Files = &synced
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
