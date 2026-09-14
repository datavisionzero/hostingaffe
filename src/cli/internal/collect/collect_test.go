package collect

import (
	"context"
	"encoding/json"
	"errors"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"
)

// aHost writes the files a Linux machine has, so that the collector is tested
// against a filesystem rather than against whatever ran the tests.
func aHost(t *testing.T, files map[string]string) string {
	t.Helper()
	root := t.TempDir()
	for path, content := range files {
		full := filepath.Join(root, path)
		if err := os.MkdirAll(filepath.Dir(full), 0o755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(full, []byte(content), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	return root
}

func anOrdinaryHost(t *testing.T) string {
	return aHost(t, map[string]string{
		"proc/sys/kernel/hostname":  "ex44\n",
		"proc/sys/kernel/osrelease": "6.14.0-27-generic\n",
		"proc/uptime":               "1893244.15 7488512.22\n",
		"proc/loadavg":              "0.14 0.20 0.18 1/842 30211\n",
		"proc/meminfo": strings.Join([]string{
			"MemTotal:       65850000 kB",
			"MemFree:        20000000 kB",
			"MemAvailable:   45800000 kB",
			"SwapTotal:       2000000 kB",
			"SwapFree:        1500000 kB",
		}, "\n") + "\n",
		"proc/mounts": strings.Join([]string{
			"/dev/nvme0n1p2 / ext4 rw,relatime 0 0",
			"tmpfs /run tmpfs rw,nosuid 0 0",
			"overlay /var/lib/docker/overlay2/abc/merged overlay rw 0 0",
			"/dev/nvme0n1p1 /boot/efi vfat rw 0 0",
			"/dev/sdb1 /srv/my\\040data ext4 rw 0 0",
		}, "\n") + "\n",
		"etc/os-release": "NAME=\"Ubuntu\"\nPRETTY_NAME=\"Ubuntu 26.04 LTS\"\nID=ubuntu\n",
		"proc/net/tcp":   procNetTcp,
		"proc/net/tcp6":  procNetTcp6,
		"proc/net/udp":   procNetUdp,
		"proc/net/udp6":  "  sl  local_address rem_address st tx_queue rx_queue tr tm->when retrnsmt uid timeout inode\n",
	})
}

// The four files the kernel keeps, as a host with SSH, a proxy, logaffe behind
// it and a resolver would have them. Ports are hex and addresses are hex words
// in the host's byte order, which is what makes them worth a fixture.
//
//	0016 = 22, 01BB = 443, 0050 = 80, 4846 = 18502, 0035 = 53, 0143 = 323
const procNetTcp = `  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
   0: 00000000:0016 00000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 12345 1 x 100 0 0 10 0
   1: 0100007F:4846 00000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 12346 1 x 100 0 0 10 0
   2: 0100007F:0016 00000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 12347 1 x 100 0 0 10 0
   3: 0100007F:8B0A 0100007F:CE4E 01 00000000:00000000 00:00000000 00000000     0        0 12348 1 x 100 0 0 10 0
`

const procNetTcp6 = `  sl  local_address                         remote_address                        st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
   0: 00000000000000000000000000000000:01BB 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 22345 1 x 100 0 0 10 0
   1: 00000000000000000000000001000000:0050 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 22346 1 x 100 0 0 10 0
`

const procNetUdp = `  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
   0: 00000000:0035 00000000:0000 07 00000000:00000000 00:00000000 00000000     0        0 32345 2 x 0
   1: 0100007F:0143 00000000:0000 07 00000000:00000000 00:00000000 00000000     0        0 32346 2 x 0
   2: 0100007F:9C40 0100007F:0035 01 00000000:00000000 00:00000000 00000000     0        0 32347 2 x 0
`

const df = `Filesystem     1B-blocks        Used   Available Capacity Mounted on
/dev/nvme0n1p2 502000000000 301000000000 175000000000      60% /
tmpfs            3400000000      2000000   3398000000       1% /run
overlay        502000000000 301000000000 175000000000      60% /var/lib/docker/overlay2/abc/merged
/dev/nvme0n1p1  1073741824    130000000   943741824      13% /boot/efi
/dev/sdb1      100000000000  40000000000  60000000000      40% /srv/my data
`

const dockerPs = `{"Command":"\"/logaffe\"","CreatedAt":"2026-09-10 09:12:00 +0000 UTC","ID":"aaaa","Image":"ghcr.io/datavisionzero/logaffe:1.4.0","Names":"logaffe","Ports":"127.0.0.1:18502->8080/tcp","State":"running","Status":"Up 3 days (healthy)"}
{"Command":"\"caddy\"","CreatedAt":"2026-08-01 09:12:00 +0000 UTC","ID":"bbbb","Image":"caddy:2.10","Names":"caddy,proxy","Ports":"0.0.0.0:443->443/tcp, 0.0.0.0:80->80/tcp","State":"exited","Status":"Exited (0) 2 hours ago"}
`

const dockerInspect = "0\t2026-09-10T09:12:00.123456789Z\thealthy\n3\t2026-08-01T09:12:00Z\t\n"

// runner answers the commands a host would, and records that it was never asked
// for anything else.
func runner(t *testing.T, answers map[string]string, failures map[string]error) func(context.Context, string, ...string) ([]byte, error) {
	t.Helper()
	return func(_ context.Context, name string, args ...string) ([]byte, error) {
		key := name
		if len(args) > 0 {
			key = name + " " + args[0]
		}
		if err, refuses := failures[key]; refuses {
			return nil, err
		}
		if answer, known := answers[key]; known {
			return []byte(answer), nil
		}
		t.Fatalf("nothing asked for %q", key)
		return nil, nil
	}
}

func ordinary(t *testing.T) Environment {
	return Environment{
		Root: anOrdinaryHost(t),
		Run: runner(t, map[string]string{
			"uname -m":       "x86_64\n",
			"df -P":          df,
			"docker ps":      dockerPs,
			"docker inspect": dockerInspect,
		}, nil),
		Now: func() time.Time { return time.Date(2026, 9, 13, 8, 0, 7, 0, time.UTC) },
	}
}

func TestAHostWithDockerSaysEverythingItCan(t *testing.T) {
	report := Collect(context.Background(), ordinary(t), "0.4.0")

	if len(report.Missing) != 0 {
		t.Fatalf("something was missing: %+v", report.Missing)
	}
	if report.Host.Hostname != "ex44" || report.Host.Os != "Ubuntu 26.04 LTS" || report.Host.Arch != "x86_64" {
		t.Fatalf("host: %+v", report.Host)
	}
	if report.Host.Kernel != "6.14.0-27-generic" || *report.Host.Uptime != 1893244 {
		t.Fatalf("host: %+v", report.Host)
	}
	if *report.Host.Load1 != 0.14 || *report.Host.Load15 != 0.18 {
		t.Fatalf("load: %+v", report.Host)
	}

	// /proc/meminfo counts in kibibytes and the report counts in bytes.
	if *report.Memory.Total != 65850000*1024 || *report.Memory.Used != (65850000-45800000)*1024 {
		t.Fatalf("memory: %+v", report.Memory)
	}
	if *report.Memory.SwapUsed != 500000*1024 {
		t.Fatalf("swap: %+v", report.Memory)
	}
}

// tmpfs and an overlay are not what a person means when they ask how full the
// machine is; a mount whose path has a space in it is.
func TestOnlyRealMountsAreDisks(t *testing.T) {
	report := Collect(context.Background(), ordinary(t), "0.4.0")

	mounts := []string{}
	for _, disk := range report.Disks {
		mounts = append(mounts, disk.Mount)
	}
	want := []string{"/", "/boot/efi", "/srv/my data"}
	if strings.Join(mounts, ",") != strings.Join(want, ",") {
		t.Fatalf("mounts: %v", mounts)
	}

	root := report.Disks[0]
	if root.Device != "/dev/nvme0n1p2" || *root.Size != 502000000000 || *root.Percent != 60 {
		t.Fatalf("root: %+v", root)
	}
}

func TestContainersCarryTheirTagAndWhatPsDoesNotSay(t *testing.T) {
	report := Collect(context.Background(), ordinary(t), "0.4.0")

	if len(report.Containers) != 2 {
		t.Fatalf("containers: %+v", report.Containers)
	}

	logaffe := report.Containers[0]
	if logaffe.Name != "logaffe" || logaffe.Image != "ghcr.io/datavisionzero/logaffe:1.4.0" {
		t.Fatalf("logaffe: %+v", logaffe)
	}
	if logaffe.State != "running" || logaffe.Health != "healthy" || *logaffe.Restarts != 0 {
		t.Fatalf("logaffe: %+v", logaffe)
	}
	if logaffe.StartedAt == nil || logaffe.StartedAt.Year() != 2026 {
		t.Fatalf("logaffe started: %+v", logaffe.StartedAt)
	}
	if len(logaffe.Ports) != 1 || logaffe.Ports[0] != "127.0.0.1:18502->8080/tcp" {
		t.Fatalf("ports: %v", logaffe.Ports)
	}

	// Several names is the first one, which is the one a person uses.
	if report.Containers[1].Name != "caddy" || *report.Containers[1].Restarts != 3 {
		t.Fatalf("caddy: %+v", report.Containers[1])
	}
}

// A host without Docker reports no containers and says why. It does not fail:
// the sign of life is the point (ADR 0015).
func TestAHostWithoutDockerSaysSoAndCarriesOn(t *testing.T) {
	env := ordinary(t)
	env.Run = runner(t,
		map[string]string{"uname -m": "x86_64\n", "df -P": df},
		map[string]error{"docker ps": &exec.Error{Name: "docker", Err: exec.ErrNotFound}})

	report := Collect(context.Background(), env, "0.4.0")

	if report.Containers != nil {
		t.Fatalf("containers: %+v", report.Containers)
	}
	if len(report.Missing) != 1 || report.Missing[0].Section != "containers" {
		t.Fatalf("missing: %+v", report.Missing)
	}
	if !strings.Contains(report.Missing[0].Reason, "not installed") {
		t.Fatalf("reason: %q", report.Missing[0].Reason)
	}
	if report.Host == nil || report.Memory == nil || report.Disks == nil {
		t.Fatal("the rest of the report went missing with the containers")
	}
}

func TestAnUnreadableSocketSaysWhatToDoAboutIt(t *testing.T) {
	env := ordinary(t)
	env.Run = runner(t,
		map[string]string{"uname -m": "x86_64\n", "df -P": df},
		map[string]error{"docker ps": &exec.ExitError{
			Stderr: []byte("permission denied while trying to connect to the Docker daemon socket\n"),
		}})

	report := Collect(context.Background(), env, "0.4.0")

	if len(report.Missing) != 1 || !strings.Contains(report.Missing[0].Reason, "docker group") {
		t.Fatalf("missing: %+v", report.Missing)
	}
}

// A machine with no /proc at all still hands in what it has, and names every
// section it could not determine.
func TestAHostThatSaysNothingStillReports(t *testing.T) {
	env := Environment{
		Root: t.TempDir(),
		Run: runner(t, nil, map[string]error{
			"uname -m":  errors.New("no uname"),
			"df -P":     errors.New("no df"),
			"docker ps": errors.New("no docker"),
		}),
		Now: func() time.Time { return time.Date(2026, 9, 13, 8, 0, 7, 0, time.UTC) },
	}

	report := Collect(context.Background(), env, "0.4.0")

	sections := []string{}
	for _, missing := range report.Missing {
		sections = append(sections, missing.Section)
	}
	if strings.Join(sections, ",") != "host,memory,disks,containers,listening,updates" {
		t.Fatalf("missing: %v", sections)
	}
	if !report.CollectedAt.Equal(time.Date(2026, 9, 13, 8, 0, 7, 0, time.UTC)) {
		t.Fatalf("collected at %v", report.CollectedAt)
	}
}

// What `collect` prints is what `send` sends: the body is built from the same
// value, so the transparency path is the path.
func TestTheBodyIsWhatWasCollected(t *testing.T) {
	report := Collect(context.Background(), ordinary(t), "0.4.0")
	body := report.Body()

	if *body.Agent != "0.4.0" || !body.CollectedAt.Equal(report.CollectedAt) {
		t.Fatalf("body: %+v", body)
	}
	if *(*body.Containers)[0].Image != "ghcr.io/datavisionzero/logaffe:1.4.0" {
		t.Fatalf("containers: %+v", *body.Containers)
	}
	if len(*body.Disks) != len(report.Disks) || len(*body.Missing) != 0 {
		t.Fatalf("body: %+v", body)
	}

	// Nothing of a container but the fields the contract names: no environment,
	// no command line (docs/cli.md).
	printed, err := json.Marshal(report)
	if err != nil {
		t.Fatal(err)
	}
	for _, forbidden := range []string{"Command", "Env", "Labels", "Mounts"} {
		if strings.Contains(string(printed), forbidden) {
			t.Fatalf("%q reached the report: %s", forbidden, printed)
		}
	}
}

// What listens, from the four files the kernel keeps: no `ss`, no package and
// no root, which is the promise of HOST-19 and the reason the section carries
// no process name.
func TestWhatListensComesOutOfProcAndCarriesNoProcess(t *testing.T) {
	report := Collect(context.Background(), ordinary(t), "0.4.0")

	got := []string{}
	for _, one := range report.Listening {
		got = append(got, one.Protocol+"/"+strconv.Itoa(one.Port)+":"+one.Binding)
	}

	// Ordered by port, so that two reports of an unchanged machine are the
	// same text. 22 is bound to the wildcard *and* to loopback and is one
	// entry: the widest bind is the honest answer.
	want := []string{
		"tcp/22:public",
		"udp/53:public",
		"tcp/80:loopback",
		"udp/323:loopback",
		"tcp/443:public",
		"tcp/18502:loopback",
	}
	if strings.Join(got, " ") != strings.Join(want, " ") {
		t.Fatalf("listening: %v", got)
	}

	// A socket with a peer is a conversation, not a door: neither the
	// established TCP connection nor the UDP one is in the list.
	for _, one := range report.Listening {
		if one.Port == 35594 || one.Port == 40000 {
			t.Fatalf("a connected socket was reported: %+v", one)
		}
	}

	printed, err := json.Marshal(report)
	if err != nil {
		t.Fatal(err)
	}
	for _, forbidden := range []string{"process", "cmdline", "exe", "uid"} {
		if strings.Contains(strings.ToLower(string(printed)), forbidden) {
			t.Fatalf("%q reached the report: %s", forbidden, printed)
		}
	}
}

// A machine whose /proc/net cannot be read says so and carries on; the section
// is missing rather than empty, because an empty one reads as "nothing
// listens".
func TestAHostWithoutProcNetSaysSoRatherThanNothingListens(t *testing.T) {
	env := ordinary(t)
	for _, name := range []string{"tcp", "tcp6", "udp", "udp6"} {
		if err := os.Remove(filepath.Join(env.Root, "proc", "net", name)); err != nil {
			t.Fatal(err)
		}
	}

	report := Collect(context.Background(), env, "0.4.0")

	if report.Listening != nil {
		t.Fatalf("listening: %+v", report.Listening)
	}
	if len(report.Missing) != 1 || report.Missing[0].Section != "listening" {
		t.Fatalf("missing: %+v", report.Missing)
	}
}

// The marker is Debian's and Ubuntu's. Its presence is a pending restart, its
// absence on such a host is none, and anything else is a missing section rather
// than a comforting false.
func TestTheRestartIsToldWhereItCanBeAndNotWhereItCannot(t *testing.T) {
	for _, one := range []struct {
		name    string
		marker  string
		release string
		want    *bool
		missing bool
	}{
		{name: "ubuntu, nothing pending", release: "ID=ubuntu\n", want: no()},
		{name: "ubuntu, waiting", marker: "run/reboot-required", release: "ID=ubuntu\n", want: yes()},
		{name: "the older path", marker: "var/run/reboot-required", release: "ID=ubuntu\n", want: yes()},
		{name: "a derivative says it is like debian", release: "ID=raspbian\nID_LIKE=debian\n", want: no()},
		{name: "alpine cannot be told", release: "ID=alpine\n", missing: true},
		{name: "the marker is believed anywhere", marker: "run/reboot-required", release: "ID=alpine\n", want: yes()},
	} {
		t.Run(one.name, func(t *testing.T) {
			files := map[string]string{"etc/os-release": one.release}
			if one.marker != "" {
				files[one.marker] = ""
			}
			env := Environment{Root: aHost(t, files), Now: time.Now}

			updates, err := collectUpdates(env)
			if one.missing {
				if err == nil {
					t.Fatalf("updates: %+v", updates)
				}
				if !strings.Contains(err.Error(), "Debian and Ubuntu") {
					t.Fatalf("reason: %q", err)
				}
				return
			}
			if err != nil {
				t.Fatal(err)
			}
			if updates.RebootRequired != *one.want {
				t.Fatalf("reboot required: %v", updates.RebootRequired)
			}
		})
	}
}

func yes() *bool { value := true; return &value }

func no() *bool { value := false; return &value }

// The body carries what was collected, the listening section included, and it
// carries it as the contract spells it.
func TestTheBodyCarriesWhatListensAndTheRestart(t *testing.T) {
	report := Collect(context.Background(), ordinary(t), "0.4.0")
	body := report.Body()

	if body.Listening == nil || len(*body.Listening) != len(report.Listening) {
		t.Fatalf("listening: %+v", body.Listening)
	}
	first := (*body.Listening)[0]
	if *first.Port != 22 || string(*first.Protocol) != "tcp" || string(*first.Binding) != "public" {
		t.Fatalf("first: %+v", first)
	}
	if body.Updates == nil || *body.Updates.RebootRequired {
		t.Fatalf("updates: %+v", body.Updates)
	}
}
