package collect

import (
	"context"
	"encoding/json"
	"errors"
	"os"
	"os/exec"
	"path/filepath"
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
	})
}

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
	if strings.Join(sections, ",") != "host,memory,disks,containers" {
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
