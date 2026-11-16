package dispatcher

import (
	"encoding/binary"
	"fmt"
	"net"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"
)

func TestAppNamePreparationAndRestrictions(t *testing.T) {
	for _, xml := range []bool{false, true} {
		config := &Config{AppName: "Example.Family_1234567890123!App", Args: []string{"", "with spaces"}}
		if xml {
			config = &Config{XML: map[string]string{"APP_NAME": config.AppName, "ARGV0": "", "ARGV1": "with spaces"}}
		}
		plan, err := prepare(config, filepath.Join(t.TempDir(), "tool.exe"), []string{"cli"}, false, map[string]string{"DISPATCHER_tool_FLAVOR": "ignored-for-app"}, time.Now())
		if err != nil {
			t.Fatal(err)
		}
		if plan.AppName != "Example.Family_1234567890123!App" || plan.Executable != "" || !reflect.DeepEqual(plan.Args, []string{"", "with spaces", "cli"}) {
			t.Fatal("incorrect APP_NAME plan")
		}
	}
	for _, name := range []string{"", "invalid", "!App", "Family!", "Family!App!Other", "Family!App\x00"} {
		if validateAppName(name) == nil {
			t.Fatal("invalid APP_NAME accepted")
		}
	}
	for _, suffix := range []string{"path: child.exe", "path_flavors: {new: child.exe}", "as_service: true", "as_desktop_user: auto", "as_user: true", "service_restart_on_network_change: true"} {
		config, err := parseYAML([]byte("app_name: Example!App\n" + suffix))
		if err != nil {
			t.Fatal(err)
		}
		if _, err := prepare(config, filepath.Join(t.TempDir(), "tool.exe"), nil, false, nil, time.Now()); err == nil {
			t.Fatal("unsupported APP_NAME combination accepted")
		}
	}
}

func manifestFixture(t *testing.T, version, appID, entry, behavior, executable string) string {
	t.Helper()
	directory := t.TempDir()
	body := fmt.Sprintf(`<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"><Identity Version="%s"/><Applications><Application Id="%s" Executable="%s" EntryPoint="%s" uap10:RuntimeBehavior="%s"/></Applications></Package>`, version, appID, executable, entry, behavior)
	writeConfig(t, filepath.Join(directory, "AppxManifest.xml"), body)
	if executable != "" {
		writeConfig(t, filepath.Join(directory, executable), "probe")
	}
	return directory
}

func TestPackageManifestResolution(t *testing.T) {
	old := manifestFixture(t, "1.0.0.0", "App", "Windows.FullTrustApplication", "", "old.exe")
	newer := manifestFixture(t, "2.0.0.0", "App", "", "packagedClassicApp", "new.exe")
	other := manifestFixture(t, "9.0.0.0", "Other", "Windows.FullTrustApplication", "", "other.exe")
	path, err := selectPackageExecutable("Family!App", []string{old, other, newer})
	if err != nil {
		t.Fatal(err)
	}
	if path != filepath.Join(newer, "new.exe") {
		t.Fatal("newest matching desktop application not selected")
	}
	uwp := manifestFixture(t, "3.0.0.0", "App", "Example.Uwp", "windowsApp", "uwp.exe")
	if _, err := selectPackageExecutable("Family!App", []string{old, uwp}); err == nil || !strings.Contains(err.Error(), "FullTrust") {
		t.Fatal("non-desktop newest application accepted")
	}
	if _, err := selectPackageExecutable("Family!Missing", []string{old}); err == nil {
		t.Fatal("missing application accepted")
	}
	missing := manifestFixture(t, "1.0.0.0", "App", "Windows.FullTrustApplication", "", "")
	if _, err := selectPackageExecutable("Family!App", []string{missing}); err == nil {
		t.Fatal("missing executable accepted")
	}
}

type fragmentedPackageConn struct{ net.Conn }

func (c fragmentedPackageConn) Read(body []byte) (int, error) {
	if len(body) > 2 {
		body = body[:2]
	}
	return c.Conn.Read(body)
}

func TestPackageFramesPreserveEnvironmentAndHandles(t *testing.T) {
	server, client := net.Pipe()
	defer server.Close()
	defer client.Close()
	request := packageRequest{Args: []string{"", "é漢字🧪", `C:\with spaces\`}, Handles: [3]uint64{1, 0x123456789, 3}, Env: map[string]string{"TOKEN": "synthetic-secret", "EMPTY": ""}}
	written := make(chan error, 1)
	go func() { written <- writePackageMessage(server, request, time.Second) }()
	var decoded packageRequest
	if err := readPackageMessage(fragmentedPackageConn{client}, &decoded, time.Second); err != nil {
		t.Fatal(err)
	}
	if err := <-written; err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(decoded, request) {
		t.Fatal("package transport altered values")
	}
}

func TestPackageFrameDeadlinesAndMalformedLength(t *testing.T) {
	server, client := net.Pipe()
	var message packageHello
	if err := readPackageMessage(server, &message, 20*time.Millisecond); err == nil {
		t.Fatal("stalled read did not time out")
	}
	server.Close()
	client.Close()
	for _, size := range []uint32{0, maximumPackageMessage + 1} {
		server, client := net.Pipe()
		go func() {
			defer client.Close()
			var header [4]byte
			binary.LittleEndian.PutUint32(header[:], size)
			client.Write(header[:])
		}()
		if err := readPackageMessage(server, &message, time.Second); err == nil {
			t.Fatal("invalid message length accepted")
		}
		server.Close()
	}
	server, client = net.Pipe()
	if err := writePackageMessage(server, message, 20*time.Millisecond); err == nil {
		t.Fatal("blocked write did not time out")
	}
	server.Close()
	client.Close()
}

func TestAppNameNativeEnvironmentPreparation(t *testing.T) {
	t.Setenv("HOME", t.TempDir())
	t.Setenv("USERPROFILE", t.TempDir())
	config, err := parseYAML([]byte("app_name: Family!App\nenv: {STATIC: explicit}\nvauth:\n  vault_addr: https://synthetic.invalid\n  env:\n    map:\n      FROM_VAUTH: native\n      STATIC: dynamic\n"))
	if err != nil {
		t.Fatal(err)
	}
	plan, err := prepare(config, filepath.Join(t.TempDir(), "tool.exe"), []string{"cli"}, false, map[string]string{}, time.Now())
	if err != nil {
		t.Fatal(err)
	}
	if plan.AppName != "Family!App" || plan.Env["FROM_VAUTH"] != "native" || plan.Env["STATIC"] != "explicit" {
		t.Fatal("APP_NAME did not retain native vauth exports/static override priority")
	}
}

func TestAppNameYAMLExpansionUsesFinalEnvironment(t *testing.T) {
	config, err := parseYAML([]byte("app_name: '%SELECTED_APP%'\nenv: {SELECTED_APP: 'Family!App'}"))
	if err != nil {
		t.Fatal(err)
	}
	plan, err := prepare(config, filepath.Join(t.TempDir(), "tool.exe"), nil, false, map[string]string{}, time.Now())
	if err != nil {
		t.Fatal(err)
	}
	if plan.AppName != "Family!App" {
		t.Fatal("APP_NAME did not use the final YAML environment")
	}
}
