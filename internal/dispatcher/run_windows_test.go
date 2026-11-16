//go:build windows

package dispatcher

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	"golang.org/x/sys/windows"
)

func TestWindowsProcessHelper(t *testing.T) {
	if os.Getenv("DISPATCHER_WINDOWS_HELPER") != "1" {
		return
	}
	mode := os.Getenv("HELPER_MODE")
	if mode == "sleep" {
		if file := os.Getenv("PID_FILE"); file != "" {
			os.WriteFile(file, []byte(strconv.Itoa(os.Getpid())), 0600)
		}
		time.Sleep(3 * time.Second)
		os.Exit(0)
	}
	if mode == "tree" {
		exe, _ := os.Executable()
		child := exec.Command(exe, "-test.run=^TestWindowsProcessHelper$")
		child.Env = append(os.Environ(), "HELPER_MODE=sleep")
		if err := child.Start(); err != nil {
			os.Exit(9)
		}
		os.WriteFile(os.Getenv("PID_FILE"), []byte(strconv.Itoa(child.Process.Pid)), 0600)
		os.Exit(0)
	}
	args := os.Args
	for i, arg := range args {
		if arg == "--" {
			args = args[i+1:]
			break
		}
	}
	cwd, _ := os.Getwd()
	input := ""
	if os.Getenv("TEST_READ_STDIN") == "1" {
		body, _ := io.ReadAll(os.Stdin)
		input = string(body)
	}
	identity := ""
	if os.Getenv("TEST_PACKAGE_IDENTITY") == "1" {
		identity = packageIdentity()
	}
	json.NewEncoder(os.Stdout).Encode(map[string]any{"args": args, "cwd": cwd, "value": os.Getenv("TEST_VALUE"), "helper_only": os.Getenv("PACKAGE_HELPER_ONLY"), "package": identity, "input": input})
	fmt.Fprintln(os.Stderr, "stderr-line")
	code, _ := strconv.Atoi(os.Getenv("HELPER_EXIT"))
	os.Exit(code)
}

func helperPlan(t *testing.T) *Plan {
	t.Helper()
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	environ := map[string]string{}
	for _, entry := range os.Environ() {
		if name, value, ok := strings.Cut(entry, "="); ok && name != "" {
			envSet(environ, name, value)
		}
	}
	envSet(environ, "DISPATCHER_WINDOWS_HELPER", "1")
	return &Plan{Executable: exe, Args: []string{"-test.run=^TestWindowsProcessHelper$", "--"}, Env: environ, CWD: t.TempDir(), Output: filepath.Join(t.TempDir(), "output.log"), UseJob: true, ShowWindow: true}
}

func TestWindowsArgumentsEnvironmentCWDOutputAndExit(t *testing.T) {
	plan := helperPlan(t)
	want := []string{"", "a b", `quote"inside`, `C:\with spaces\`, "été 🧪", "a&b"}
	plan.Args = append(plan.Args, want...)
	plan.Env["TEST_VALUE"], plan.Env["HELPER_EXIT"] = "explicit", "17"
	code, err := Run(plan)
	if err != nil {
		t.Fatal(err)
	}
	if code != 17 {
		t.Fatalf("wrong exit code: %d", code)
	}
	body, err := os.ReadFile(plan.Output)
	if err != nil {
		t.Fatal(err)
	}
	line := strings.SplitN(string(body), "\n", 2)
	var result struct {
		Args  []string
		CWD   string
		Value string
	}
	if err := json.Unmarshal([]byte(line[0]), &result); err != nil {
		t.Fatal(err)
	}
	if len(result.Args) != len(want) {
		t.Fatalf("wrong arguments: %#v", result.Args)
	}
	for i := range want {
		if result.Args[i] != want[i] {
			t.Fatalf("argument %d changed", i)
		}
	}
	if result.Value != "explicit" || !strings.EqualFold(result.CWD, plan.CWD) || !strings.Contains(string(body), "stderr-line") {
		t.Fatal("environment/CWD/output forwarding failed")
	}
	plan.Env["HELPER_EXIT"] = "0"
	if _, err := Run(plan); err != nil {
		t.Fatal(err)
	}
	body, _ = os.ReadFile(plan.Output)
	if strings.Count(string(body), "stderr-line") != 2 {
		t.Fatal("OUTPUT was overwritten instead of appended")
	}
}

func TestWindowsJobCleansDescendant(t *testing.T) {
	plan := helperPlan(t)
	plan.Env["HELPER_MODE"], plan.Env["PID_FILE"] = "tree", filepath.Join(t.TempDir(), "pid")
	code, err := Run(plan)
	if err != nil || code != 0 {
		t.Fatalf("tree launch: %d %v", code, err)
	}
	body, err := os.ReadFile(plan.Env["PID_FILE"])
	if err != nil {
		t.Fatal(err)
	}
	pid, _ := strconv.Atoi(string(body))
	handle, err := windows.OpenProcess(windows.SYNCHRONIZE, false, uint32(pid))
	if err == windows.ERROR_INVALID_PARAMETER {
		return
	} // Already gone.
	if err != nil {
		t.Fatal(err)
	}
	defer windows.CloseHandle(handle)
	status, err := windows.WaitForSingleObject(handle, 2000)
	if err != nil || status != windows.WAIT_OBJECT_0 {
		t.Fatal("job descendant remained alive")
	}
}

func TestWindowsDetachedReturnsWithoutWaiting(t *testing.T) {
	plan := helperPlan(t)
	plan.Env["HELPER_MODE"] = "sleep"
	plan.Env["PID_FILE"] = filepath.Join(t.TempDir(), "pid")
	plan.Detached = true
	start := time.Now()
	if _, err := Run(plan); err != nil {
		t.Fatal(err)
	}
	if time.Since(start) > 2*time.Second {
		t.Fatal("DETACHED waited for the child")
	}
	// Reap the synthetic detached child before Windows removes its open OUTPUT file.
	var body []byte
	for deadline := time.Now().Add(2 * time.Second); time.Now().Before(deadline); {
		body, _ = os.ReadFile(plan.Env["PID_FILE"])
		if len(body) != 0 {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}
	if len(body) == 0 {
		t.Fatal("detached child did not start")
	}
	pid, _ := strconv.Atoi(string(body))
	handle, err := windows.OpenProcess(windows.PROCESS_TERMINATE|windows.SYNCHRONIZE, false, uint32(pid))
	if err != nil {
		t.Fatal(err)
	}
	defer windows.CloseHandle(handle)
	if err := windows.TerminateProcess(handle, 0); err != nil {
		t.Fatal(err)
	}
	if _, err := windows.WaitForSingleObject(handle, 2000); err != nil {
		t.Fatal(err)
	}
}

func TestWindowsCreateFailure(t *testing.T) {
	plan := helperPlan(t)
	plan.Executable = filepath.Join(t.TempDir(), "missing.exe")
	if _, err := Run(plan); err == nil {
		t.Fatal("missing executable reported success")
	}
}

func TestWindowsRenamedBinariesXMLYAMLAndCLI(t *testing.T) {
	directory := os.Getenv("DISPATCHER_GO_TEST_OUTPUT")
	if directory == "" {
		t.Skip("binary smoke requires ./build --build output")
	}
	for _, name := range []string{"dispatcher_go_cmd.exe", "dispatcher_go_cmd_x64.exe", "dispatcher_go_win.exe", "dispatcher_go_win_x64.exe"} {
		for _, format := range []string{"xml", "yaml"} {
			t.Run(name+"/"+format, func(t *testing.T) {
				body, err := os.ReadFile(filepath.Join(directory, name))
				if err != nil {
					t.Fatal(err)
				}
				launcher := filepath.Join(t.TempDir(), "renamed.exe")
				if err := os.WriteFile(launcher, body, 0700); err != nil {
					t.Fatal(err)
				}
				target, err := os.Executable()
				if err != nil {
					t.Fatal(err)
				}
				output := filepath.Join(t.TempDir(), "target.json")
				var config string
				if format == "yaml" {
					config = "path: '" + target + "'\nargs: ['-test.run=^TestWindowsProcessHelper$', '--', 'injected']\nenv: {DISPATCHER_WINDOWS_HELPER: '1', TEST_VALUE: 'native'}\nuse_showwindow: true\noutput: '" + output + "'\n"
				} else {
					config = xmlStart + `<add key="PATH" value="` + target + `"/><add key="ARGV0" value="-test.run=^TestWindowsProcessHelper$"/><add key="ARGV1" value="--"/><add key="ARGV2" value="injected"/><add key="ENV_DISPATCHER_WINDOWS_HELPER" value="1"/><add key="ENV_TEST_VALUE" value="native"/><add key="OUTPUT" value="` + output + `"/>` + xmlEnd
				}
				writeConfig(t, launcher+".config", config)
				cmd := exec.Command(launcher, "cli space", "")
				if output, err := cmd.CombinedOutput(); err != nil {
					t.Fatalf("launcher failed: %v %s", err, output)
				}
				body, err = os.ReadFile(output)
				if err != nil {
					t.Fatal(err)
				}
				var result struct {
					Args  []string
					Value string
				}
				if err := json.Unmarshal([]byte(strings.SplitN(string(body), "\n", 2)[0]), &result); err != nil {
					t.Fatal(err)
				}
				if len(result.Args) != 3 || result.Args[0] != "injected" || result.Args[1] != "cli space" || result.Args[2] != "" || result.Value != "native" {
					t.Fatal("renamed binary forwarding failed")
				}
			})
		}
	}
}
