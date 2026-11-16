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
	"unsafe"

	winio "github.com/Microsoft/go-winio"
	"golang.org/x/sys/windows"
)

func TestPackageHelperProcess(t *testing.T) {
	for i, arg := range os.Args {
		if arg != "--package-helper-test" {
			continue
		}
		mode, pipe, target, parent := os.Args[i+1], os.Args[i+2], os.Args[i+3], os.Args[i+4]
		if mode == "exit" {
			os.Exit(9)
		}
		if mode == "hold" {
			time.Sleep(time.Hour)
			os.Exit(0)
		}
		os.Setenv("PACKAGE_HELPER_ONLY", "must-not-leak")
		pid, _ := strconv.ParseUint(parent, 10, 32)
		if err := PackageHelper(pipe, target, uint32(pid)); err != nil {
			os.Exit(1)
		}
		os.Exit(0)
	}
}

func spawnPackageTestHelper(t *testing.T, mode, pipe, target string) windows.Handle {
	t.Helper()
	self, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	command := exec.Command(self, "-test.run=^TestPackageHelperProcess$", "--", "--package-helper-test", mode, pipe, target, strconv.Itoa(os.Getpid()))
	command.Stdout, command.Stderr = io.Discard, io.Discard
	if err := command.Start(); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { command.Wait() })
	handle, err := openPackageHelper(uint32(command.Process.Pid))
	if err != nil {
		t.Fatal(err)
	}
	return handle
}

func packageIdentity() string {
	getName := packageKernel.NewProc("GetCurrentPackageFullName")
	var length uint32
	code, _, _ := getName.Call(uintptr(unsafe.Pointer(&length)), 0)
	if code != uintptr(windows.ERROR_INSUFFICIENT_BUFFER) {
		return ""
	}
	name := make([]uint16, length)
	code, _, _ = getName.Call(uintptr(unsafe.Pointer(&length)), uintptr(unsafe.Pointer(&name[0])))
	if code != 0 {
		return ""
	}
	return windows.UTF16ToString(name)
}

func TestPackageTransportEnvironmentArgumentsAndExit(t *testing.T) {
	plan := helperPlan(t)
	plan.Args = append(plan.Args, "", "spaces é漢字", `C:\trailing slash\`)
	plan.Env["TEST_VALUE"], plan.Env["HELPER_EXIT"] = "explicit", "37"
	code, err := runPackage(plan, func(pipe string) (windows.Handle, error) {
		return spawnPackageTestHelper(t, "normal", pipe, plan.Executable), nil
	}, packageTimeout)
	if err != nil {
		t.Fatal(err)
	}
	if code != 37 {
		t.Fatal("package target exit code changed")
	}
	body, err := os.ReadFile(plan.Output)
	if err != nil {
		t.Fatal(err)
	}
	var result struct {
		Args       []string
		CWD        string
		Value      string
		HelperOnly string `json:"helper_only"`
	}
	if err := json.Unmarshal([]byte(strings.SplitN(string(body), "\n", 2)[0]), &result); err != nil {
		t.Fatal(err)
	}
	if len(result.Args) != 3 || result.Args[0] != "" || result.Args[1] != "spaces é漢字" || result.Args[2] != `C:\trailing slash\` {
		t.Fatal("package arguments changed")
	}
	if result.Value != "explicit" || !strings.EqualFold(result.CWD, plan.CWD) || result.HelperOnly != "" {
		t.Fatal("package ENV/CWD changed or helper environment leaked")
	}
	if !strings.Contains(string(body), "stderr-line") {
		t.Fatal("package stderr not forwarded")
	}
}

func TestPackageHelperExitAndUnexpectedPeer(t *testing.T) {
	for _, mode := range []string{"exit", "hold", "impostor"} {
		t.Run(mode, func(t *testing.T) {
			plan := helperPlan(t)
			start := time.Now()
			_, err := runPackage(plan, func(pipe string) (windows.Handle, error) {
				if mode == "impostor" {
					rogue := spawnPackageTestHelper(t, "normal", pipe, plan.Executable)
					windows.CloseHandle(rogue)
					return spawnPackageTestHelper(t, "hold", pipe, plan.Executable), nil
				}
				return spawnPackageTestHelper(t, mode, pipe, plan.Executable), nil
			}, 300*time.Millisecond)
			if err == nil || time.Since(start) > 5*time.Second {
				t.Fatalf("helper failure not bounded: %v", err)
			}
			if mode == "impostor" && !strings.Contains(err.Error(), "unexpected package helper") {
				t.Fatalf("wrong peer error: %v", err)
			}
		})
	}
}

func TestPackageCancellationKillsSuspendedTarget(t *testing.T) {
	plan := helperPlan(t)
	name := `\\.\pipe\dispatcher-go-cancel-` + strconv.Itoa(os.Getpid()) + "-" + strconv.FormatInt(time.Now().UnixNano(), 10)
	listener, err := winio.ListenPipe(name, nil)
	if err != nil {
		t.Fatal(err)
	}
	defer listener.Close()
	helper := spawnPackageTestHelper(t, "normal", name, plan.Executable)
	defer windows.CloseHandle(helper)
	conn, err := acceptPackageHelper(listener, helper, time.Second)
	if err != nil {
		t.Fatal(err)
	}
	var hello packageHello
	if err := readPackageMessage(conn, &hello, time.Second); err != nil {
		t.Fatal(err)
	}
	request := packageRequest{Args: plan.Args, CWD: plan.CWD, Output: plan.Output, ParentPID: uint32(os.Getpid()), Env: plan.Env}
	if err := writePackageMessage(conn, request, time.Second); err != nil {
		t.Fatal(err)
	}
	var created packageResult
	if err := readPackageMessage(conn, &created, time.Second); err != nil {
		t.Fatal(err)
	}
	if !created.Ready || created.PID == 0 {
		t.Fatal("suspended target was not created")
	}
	target, err := windows.OpenProcess(windows.SYNCHRONIZE, false, created.PID)
	if err != nil {
		t.Fatal(err)
	}
	defer windows.CloseHandle(target)
	conn.Close() // No ready acknowledgement: target must never execute.
	status, err := windows.WaitForSingleObject(target, 2000)
	if err != nil || status != windows.WAIT_OBJECT_0 {
		t.Fatal("cancelled target remained suspended/alive")
	}
	if body, _ := os.ReadFile(plan.Output); len(body) != 0 {
		t.Fatal("cancelled target executed")
	}
}

func TestPackageDetachedLifetimeAndJobCleanup(t *testing.T) {
	for _, detached := range []bool{false, true} {
		t.Run(strconv.FormatBool(detached), func(t *testing.T) {
			plan := helperPlan(t)
			plan.Detached = detached
			plan.Env["PID_FILE"] = filepath.Join(t.TempDir(), "pid")
			if detached {
				plan.Env["HELPER_MODE"] = "sleep"
			} else {
				plan.Env["HELPER_MODE"] = "tree"
			}
			if _, err := runPackage(plan, func(pipe string) (windows.Handle, error) {
				return spawnPackageTestHelper(t, "normal", pipe, plan.Executable), nil
			}, packageTimeout); err != nil {
				t.Fatal(err)
			}
			var body []byte
			for deadline := time.Now().Add(2 * time.Second); time.Now().Before(deadline); {
				body, _ = os.ReadFile(plan.Env["PID_FILE"])
				if len(body) != 0 {
					break
				}
				time.Sleep(10 * time.Millisecond)
			}
			if len(body) == 0 {
				t.Fatal("target did not start")
			}
			pid, _ := strconv.Atoi(string(body))
			handle, err := windows.OpenProcess(windows.SYNCHRONIZE|windows.PROCESS_TERMINATE, false, uint32(pid))
			if !detached && err == windows.ERROR_INVALID_PARAMETER {
				return
			}
			if err != nil {
				t.Fatal(err)
			}
			defer windows.CloseHandle(handle)
			if detached {
				status, err := windows.WaitForSingleObject(handle, 0)
				if err != nil || status != uint32(windows.WAIT_TIMEOUT) {
					t.Fatal("detached target did not survive helper exit")
				}
				windows.TerminateProcess(handle, 0)
			}
			status, err := windows.WaitForSingleObject(handle, 2000)
			if err != nil || status != windows.WAIT_OBJECT_0 {
				t.Fatal("target/descendant cleanup failed")
			}
		})
	}
}

func TestPackageResolverNotInstalled(t *testing.T) {
	if _, err := resolvePackageExecutable("Dispatcher.NonexistentPackage_1234567890123!App"); err == nil || !strings.Contains(err.Error(), "not installed") {
		t.Fatalf("missing package not rejected: %v", err)
	}
}

func TestRealPackageContextCOMBootstrap(t *testing.T) {
	appName := os.Getenv("DISPATCHER_GO_TEST_APP_NAME")
	if appName == "" {
		t.Skip("requires an installed desktop package identifier; never launches that application's executable")
	}
	if _, err := resolvePackageExecutable(appName); err != nil {
		t.Fatal(err)
	}
	plan := helperPlan(t)
	plan.Env["TEST_PACKAGE_IDENTITY"], plan.Env["TEST_VALUE"], plan.Env["HELPER_EXIT"] = "1", "package-env", "29"
	self, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	code, err := runPackage(plan, func(pipe string) (windows.Handle, error) {
		return startInPackage(appName, self, []string{"-test.run=^TestPackageHelperProcess$", "--", "--package-helper-test", "normal", pipe, plan.Executable, strconv.Itoa(os.Getpid())})
	}, packageTimeout)
	if err != nil {
		t.Fatal(err)
	}
	if code != 29 {
		t.Fatal("real package target exit code changed")
	}
	body, err := os.ReadFile(plan.Output)
	if err != nil {
		t.Fatal(err)
	}
	var result struct {
		Value   string
		Package string
	}
	if err := json.Unmarshal([]byte(strings.SplitN(string(body), "\n", 2)[0]), &result); err != nil {
		t.Fatal(err)
	}
	family, _, _ := strings.Cut(appName, "!")
	// Full names replace the family suffix with version/architecture; the package's name remains stable.
	prefix := strings.Split(family, "_")[0] + "_"
	if result.Value != "package-env" || !strings.HasPrefix(result.Package, prefix) {
		t.Fatalf("target did not inherit the package context: %q", result.Package)
	}
	fmt.Println("PASS native COM bootstrap and package identity on a synthetic target")
}

func TestRealPackageContextProductionHelpers(t *testing.T) {
	appName, directory := os.Getenv("DISPATCHER_GO_TEST_APP_NAME"), os.Getenv("DISPATCHER_GO_TEST_OUTPUT")
	if appName == "" || directory == "" {
		t.Skip("requires installed package context and ./build --build output; runs only our probe")
	}
	for _, name := range []string{"dispatcher_go_cmd.exe", "dispatcher_go_cmd_x64.exe", "dispatcher_go_win.exe", "dispatcher_go_win_x64.exe"} {
		t.Run(name, func(t *testing.T) {
			body, err := os.ReadFile(filepath.Join(directory, name))
			if err != nil {
				t.Fatal(err)
			}
			helper := filepath.Join(t.TempDir(), name)
			if err := os.WriteFile(helper, body, 0700); err != nil {
				t.Fatal(err)
			}
			plan := helperPlan(t)
			plan.Args = append(plan.Args, "", "spaces é漢字", `C:\path with spaces\`)
			plan.Env["TEST_PACKAGE_IDENTITY"], plan.Env["TEST_VALUE"], plan.Env["HELPER_EXIT"] = "1", "production-env", "31"
			code, err := runPackage(plan, func(pipe string) (windows.Handle, error) {
				return startInPackage(appName, helper, []string{"--dispatcher-go-package-helper", pipe, plan.Executable, strconv.Itoa(os.Getpid())})
			}, packageTimeout)
			if err != nil {
				t.Fatal(err)
			}
			if code != 31 {
				t.Fatal("production helper exit code changed")
			}
			body, err = os.ReadFile(plan.Output)
			if err != nil {
				t.Fatal(err)
			}
			var result struct {
				Args    []string
				CWD     string
				Value   string
				Package string
			}
			if err := json.Unmarshal([]byte(strings.SplitN(string(body), "\n", 2)[0]), &result); err != nil {
				t.Fatal(err)
			}
			if result.Value != "production-env" || result.Package == "" || !strings.EqualFold(result.CWD, plan.CWD) || len(result.Args) != 3 || result.Args[0] != "" || result.Args[1] != "spaces é漢字" || result.Args[2] != `C:\path with spaces\` {
				t.Fatal("production helper lost ENV/ARGV/CWD/package context")
			}
		})
	}
}

func TestPackageForwardWorker(t *testing.T) {
	if os.Getenv("PACKAGE_FORWARD_WORKER") != "1" {
		return
	}
	self, _ := os.Executable()
	environ := map[string]string{}
	for _, entry := range os.Environ() {
		if key, value, found := strings.Cut(entry, "="); found && key != "" {
			envSet(environ, key, value)
		}
	}
	delete(environ, "PACKAGE_FORWARD_WORKER")
	environ["DISPATCHER_WINDOWS_HELPER"], environ["TEST_READ_STDIN"], environ["TEST_VALUE"] = "1", "1", "forwarded"
	plan := &Plan{Executable: self, Args: []string{"-test.run=^TestWindowsProcessHelper$", "--", "forward"}, Env: environ, ShowWindow: true, UseJob: true}
	code, err := runPackage(plan, func(pipe string) (windows.Handle, error) { return spawnPackageTestHelper(t, "normal", pipe, self), nil }, packageTimeout)
	if err != nil {
		os.Exit(2)
	}
	os.Exit(int(code))
}

func TestPackageForwardsPipedStandardHandles(t *testing.T) {
	self, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	worker := exec.Command(self, "-test.run=^TestPackageForwardWorker$")
	worker.Env = append(os.Environ(), "PACKAGE_FORWARD_WORKER=1")
	worker.Stdin = strings.NewReader("stdin é漢字")
	var stdout, stderr strings.Builder
	worker.Stdout, worker.Stderr = &stdout, &stderr
	if err := worker.Run(); err != nil {
		t.Fatal(err)
	}
	var result struct {
		Input string
		Value string
	}
	if err := json.Unmarshal([]byte(stdout.String()), &result); err != nil {
		t.Fatal(err)
	}
	if result.Input != "stdin é漢字" || result.Value != "forwarded" || !strings.Contains(stderr.String(), "stderr-line") {
		t.Fatal("package standard handles not inherited")
	}
}
