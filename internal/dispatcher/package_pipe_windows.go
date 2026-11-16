//go:build windows

package dispatcher

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"fmt"
	"net"
	"os"
	"path/filepath"
	"strings"
	"time"
	"unsafe"

	winio "github.com/Microsoft/go-winio"
	"golang.org/x/sys/windows"
)

func packagePeerPID(conn net.Conn, server bool) (uint32, error) {
	file, ok := conn.(interface{ Fd() uintptr })
	if !ok {
		return 0, fmt.Errorf("invalid package pipe handle")
	}
	var pid uint32
	var err error
	if server {
		err = windows.GetNamedPipeServerProcessId(windows.Handle(file.Fd()), &pid)
	} else {
		err = windows.GetNamedPipeClientProcessId(windows.Handle(file.Fd()), &pid)
	}
	return pid, err
}

func acceptPackageHelper(listener net.Listener, helper windows.Handle, timeout time.Duration) (net.Conn, error) {
	type connection struct {
		conn net.Conn
		err  error
	}
	accepted := make(chan connection, 1)
	go func() { conn, err := listener.Accept(); accepted <- connection{conn, err} }()
	deadline := time.NewTimer(timeout)
	defer deadline.Stop()
	poll := time.NewTicker(20 * time.Millisecond)
	defer poll.Stop()
	cancel := func(err error) (net.Conn, error) {
		listener.Close()
		result := <-accepted
		if result.conn != nil {
			result.conn.Close()
		}
		return nil, err
	}
	for {
		select {
		case result := <-accepted:
			return result.conn, result.err
		case <-deadline.C:
			return cancel(fmt.Errorf("timed out entering the package context"))
		case <-poll.C:
			status, err := windows.WaitForSingleObject(helper, 0)
			if err != nil {
				return cancel(err)
			}
			if status == windows.WAIT_OBJECT_0 {
				return cancel(fmt.Errorf("package helper exited before connecting"))
			}
		}
	}
}

func runPackage(plan *Plan, bootstrap func(string) (windows.Handle, error), timeout time.Duration) (code uint32, err error) {
	cwd := plan.CWD
	if cwd == "" {
		cwd, err = os.Getwd()
		if err != nil {
			return 0, err
		}
	}
	cwd, err = filepath.Abs(cwd)
	if err != nil {
		return 0, fmt.Errorf("invalid package CWD")
	}
	if info, err := os.Stat(cwd); err != nil || !info.IsDir() {
		return 0, fmt.Errorf("package CWD does not exist")
	}
	output := plan.Output
	if output != "" {
		output, err = filepath.Abs(output)
		if err != nil {
			return 0, fmt.Errorf("invalid package OUTPUT")
		}
	}
	user, err := windows.GetCurrentProcessToken().GetTokenUser()
	if err != nil {
		return 0, err
	}
	var random [16]byte
	if _, err := rand.Read(random[:]); err != nil {
		return 0, err
	}
	name := `\\.\pipe\dispatcher-go-app-` + hex.EncodeToString(random[:])
	listener, err := winio.ListenPipe(name, &winio.PipeConfig{SecurityDescriptor: "D:P(A;;GA;;;SY)(A;;GA;;;" + user.User.Sid.String() + ")", InputBufferSize: 65536, OutputBufferSize: 65536})
	if err != nil {
		return 0, fmt.Errorf("cannot create package helper pipe: %w", err)
	}
	defer listener.Close()
	helper, err := bootstrap(name)
	if err != nil {
		return 0, err
	}
	defer windows.CloseHandle(helper)
	var conn net.Conn
	var target, job windows.Handle
	complete := false
	defer func() {
		if conn != nil {
			conn.Close()
		}
		if !complete && target != 0 {
			windows.TerminateProcess(target, 1)
		}
		if target != 0 {
			windows.CloseHandle(target)
		}
		if job != 0 {
			windows.CloseHandle(job)
		}
		if !complete {
			// Closing the pipe lets the helper abort its suspended target before exit.
			status, _ := windows.WaitForSingleObject(helper, uint32(timeout/time.Millisecond))
			if status == uint32(windows.WAIT_TIMEOUT) {
				windows.TerminateProcess(helper, 1)
				windows.WaitForSingleObject(helper, 2000)
			}
		}
	}()
	conn, err = acceptPackageHelper(listener, helper, timeout)
	if err != nil {
		return 0, err
	}
	expected, err := windows.GetProcessId(helper)
	if err != nil {
		return 0, err
	}
	peer, err := packagePeerPID(conn, false)
	if err != nil || peer != expected {
		return 0, fmt.Errorf("unexpected package helper process")
	}
	var hello packageHello
	if err := readPackageMessage(conn, &hello, timeout); err != nil {
		return 0, err
	}
	if hello.Version != 1 || hello.PID != peer || !strings.EqualFold(hello.Executable, plan.Executable) {
		return 0, fmt.Errorf("invalid package helper handshake")
	}
	if plan.UseJob && !plan.Detached {
		job, err = createJob()
		if err != nil {
			return 0, err
		}
		if err := windows.AssignProcessToJobObject(job, helper); err != nil {
			// The package host can impose a job policy. Preserve C# best-effort behavior.
			fmt.Fprintln(os.Stderr, "dispatcher: could not assign the package helper to a job:", err)
			windows.CloseHandle(job)
			job = 0
		}
	}
	request := packageRequest{Args: plan.Args, CWD: cwd, ShowWindow: plan.ShowWindow, Detached: plan.Detached, Output: output, ParentPID: uint32(os.Getpid()), Env: plan.Env}
	for i, which := range []uint32{windows.STD_INPUT_HANDLE, windows.STD_OUTPUT_HANDLE, windows.STD_ERROR_HANDLE} {
		if plan.Detached {
			break
		}
		original, err := windows.GetStdHandle(which)
		if err != nil || original == 0 || original == windows.InvalidHandle {
			continue
		}
		kind, err := windows.GetFileType(original)
		if err != nil {
			return 0, err
		}
		if kind == windows.FILE_TYPE_CHAR {
			continue
		} // The helper obtains console handles after AttachConsole.
		var copied windows.Handle
		if err := windows.DuplicateHandle(windows.CurrentProcess(), original, helper, &copied, 0, true, windows.DUPLICATE_SAME_ACCESS); err != nil {
			return 0, fmt.Errorf("cannot transfer package standard handle: %w", err)
		}
		request.Handles[i] = uint64(copied)
	}
	if err := writePackageMessage(conn, &request, timeout); err != nil {
		return 0, err
	}
	var result packageResult
	if err := readPackageMessage(conn, &result, timeout); err != nil {
		return 0, err
	}
	if !result.Ready || result.PID == 0 {
		return 0, fmt.Errorf("package helper: %s", result.Error)
	}
	target, err = windows.OpenProcess(windows.SYNCHRONIZE|windows.PROCESS_QUERY_LIMITED_INFORMATION|windows.PROCESS_TERMINATE, false, result.PID)
	if err != nil {
		return 0, fmt.Errorf("cannot monitor packaged application: %w", err)
	}
	if err := writePackageMessage(conn, packageResult{Ready: true}, timeout); err != nil {
		return 0, err
	}
	if err := readPackageMessage(conn, &result, timeout); err != nil {
		return 0, err
	}
	if !result.Ready {
		return 0, fmt.Errorf("package helper: %s", result.Error)
	}
	if !plan.Detached {
		if _, err := windows.WaitForSingleObject(target, windows.INFINITE); err != nil {
			return 0, err
		}
		if err := windows.GetExitCodeProcess(target, &code); err != nil {
			return 0, err
		}
	}
	complete = true
	return code, nil
}

// PackageHelper bypasses config/provider loading; only the parent's explicit ENV reaches the target.
func PackageHelper(pipeName, executable string, parentPID uint32) (err error) {
	ctx, cancel := context.WithTimeout(context.Background(), packageTimeout)
	defer cancel()
	conn, err := winio.DialPipeContext(ctx, pipeName)
	if err != nil {
		return fmt.Errorf("cannot connect package helper: %w", err)
	}
	defer conn.Close()
	peer, err := packagePeerPID(conn, true)
	if err != nil || peer != parentPID {
		return fmt.Errorf("unexpected package dispatcher process")
	}
	if err := writePackageMessage(conn, packageHello{Version: 1, PID: uint32(os.Getpid()), Executable: executable}, packageTimeout); err != nil {
		return err
	}
	var request packageRequest
	if err := readPackageMessage(conn, &request, packageTimeout); err != nil {
		return err
	}
	if request.ParentPID != parentPID {
		return fmt.Errorf("invalid package dispatcher identity")
	}
	for key, value := range request.Env {
		if err := validateEnv(key, value); err != nil {
			return err
		}
	}
	var owned []windows.Handle
	var process windows.ProcessInformation
	complete := false
	defer func() {
		if process.Process != 0 {
			if !complete {
				windows.TerminateProcess(process.Process, 1)
			}
			windows.CloseHandle(process.Process)
			windows.CloseHandle(process.Thread)
		}
		for _, handle := range owned {
			windows.CloseHandle(handle)
		}
		if err != nil {
			writePackageMessage(conn, packageResult{Error: err.Error()}, packageTimeout)
		}
	}()
	var handles [3]windows.Handle
	for i, value := range request.Handles {
		handles[i] = windows.Handle(value)
		if uint64(handles[i]) != value {
			return fmt.Errorf("invalid package standard handle")
		}
		if handles[i] != 0 {
			owned = append(owned, handles[i])
		}
	}
	if !request.Detached {
		attachConsole.Call(uintptr(parentPID))
		for i, which := range []uint32{windows.STD_INPUT_HANDLE, windows.STD_OUTPUT_HANDLE, windows.STD_ERROR_HANDLE} {
			if handles[i] == 0 {
				handles[i], _ = windows.GetStdHandle(which)
			}
		}
	}
	for i, handle := range handles {
		if handle == 0 || handle == windows.InvalidHandle {
			mode := os.O_WRONLY
			if i == 0 {
				mode = os.O_RDONLY
			}
			null, err := os.OpenFile("NUL", mode, 0)
			if err != nil {
				return err
			}
			copied, err := inheritHandle(null, i == 0)
			null.Close()
			if err != nil {
				return err
			}
			handles[i] = copied
			owned = append(owned, copied)
		}
	}
	if request.Output != "" {
		log, err := os.OpenFile(request.Output, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0600)
		if err != nil {
			return fmt.Errorf("cannot open package OUTPUT")
		}
		copied, err := inheritHandle(log, false)
		log.Close()
		if err != nil {
			return err
		}
		handles[1], handles[2] = copied, copied
		owned = append(owned, copied)
	}
	startup := windows.StartupInfo{Cb: uint32(unsafe.Sizeof(windows.StartupInfo{})), Flags: windows.STARTF_USESTDHANDLES, StdInput: handles[0], StdOutput: handles[1], StdErr: handles[2]}
	if request.ShowWindow {
		startup.Flags |= windows.STARTF_USESHOWWINDOW
		startup.ShowWindow = windows.SW_HIDE
	}
	plan := Plan{Executable: executable, Args: request.Args, Env: request.Env, CWD: request.CWD}
	flags := uint32(windows.CREATE_UNICODE_ENVIRONMENT | windows.CREATE_SUSPENDED)
	if request.Detached {
		flags |= windows.DETACHED_PROCESS
	}
	process, err = createSuspended(&plan, &startup, flags, !request.Detached || request.Output != "")
	if err != nil {
		return err
	}
	if err := writePackageMessage(conn, packageResult{Ready: true, PID: process.ProcessId}, packageTimeout); err != nil {
		return err
	}
	var ready packageResult
	if err := readPackageMessage(conn, &ready, packageTimeout); err != nil {
		return err
	}
	if !ready.Ready {
		return fmt.Errorf("package launch cancelled")
	}
	if _, err := windows.ResumeThread(process.Thread); err != nil {
		return fmt.Errorf("cannot resume packaged application: %w", err)
	}
	if err := writePackageMessage(conn, packageResult{Ready: true}, packageTimeout); err != nil {
		return err
	}
	complete = true
	return nil
}
