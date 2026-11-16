//go:build windows

package dispatcher

import (
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"sort"
	"strings"
	"syscall"
	"unicode/utf16"
	"unsafe"

	"golang.org/x/sys/windows"
)

func configureCommand(cmd *exec.Cmd, raw string) {
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CmdLine: raw}
}

func runPrestart(plan *Plan) error {
	if plan.Prestart != nil {
		cmd, err := commandProcess(context.Background(), *plan.Prestart, plan.Env)
		if err != nil {
			return err
		}
		cmd.Stdin, cmd.Stdout, cmd.Stderr = os.Stdin, os.Stdout, os.Stderr
		if err := cmd.Run(); err != nil {
			var exit *exec.ExitError
			if !errors.As(err, &exit) {
				return fmt.Errorf("cannot start PRESTART_CMD")
			}
		}
	}
	return nil
}

func Run(plan *Plan) (uint32, error) {
	if plan.AppName != "" {
		return runAppPackage(plan)
	}
	if err := runPrestart(plan); err != nil {
		return 0, err
	}
	path, err := lookExecutable(plan.Executable, plan.Env)
	if err != nil {
		return 0, err
	}
	local := *plan
	local.Executable = path
	flags := uint32(windows.CREATE_UNICODE_ENVIRONMENT | windows.CREATE_SUSPENDED)
	if plan.Detached {
		flags |= windows.DETACHED_PROCESS
	}
	if !plan.UseJob {
		flags |= windows.CREATE_BREAKAWAY_FROM_JOB
	}
	var job windows.Handle
	if plan.UseJob && !plan.Detached {
		job, err = createJob()
		if err != nil {
			return 0, err
		}
		defer windows.CloseHandle(job)
	}
	stdout, stderr := os.Stdout, os.Stderr
	if plan.Output != "" {
		log, err := os.OpenFile(plan.Output, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0600)
		if err != nil {
			return 0, fmt.Errorf("cannot open OUTPUT")
		}
		defer log.Close()
		stdout, stderr = log, log
	}
	var handles []windows.Handle
	defer func() {
		for _, handle := range handles {
			windows.CloseHandle(handle)
		}
	}()
	for i, file := range []*os.File{os.Stdin, stdout, stderr} {
		handle, err := inheritHandle(file, i == 0)
		if err != nil {
			return 0, fmt.Errorf("cannot inherit standard handle: %w", err)
		}
		handles = append(handles, handle)
	}
	startup := windows.StartupInfo{Cb: uint32(unsafe.Sizeof(windows.StartupInfo{})), Flags: windows.STARTF_USESTDHANDLES, StdInput: handles[0], StdOutput: handles[1], StdErr: handles[2]}
	if plan.ShowWindow {
		startup.Flags |= windows.STARTF_USESHOWWINDOW
		startup.ShowWindow = windows.SW_HIDE
	}
	process, err := createSuspended(&local, &startup, flags, true)
	if err != nil {
		return 0, err
	}
	defer windows.CloseHandle(process.Process)
	defer windows.CloseHandle(process.Thread)
	if job != 0 {
		if err := windows.AssignProcessToJobObject(job, process.Process); err != nil {
			windows.TerminateProcess(process.Process, 1)
			return 0, fmt.Errorf("cannot assign target to job: %w", err)
		}
	}
	if _, err := windows.ResumeThread(process.Thread); err != nil {
		windows.TerminateProcess(process.Process, 1)
		return 0, fmt.Errorf("cannot resume target: %w", err)
	}
	if plan.Detached {
		return 0, nil
	}
	if _, err := windows.WaitForSingleObject(process.Process, windows.INFINITE); err != nil {
		return 0, fmt.Errorf("cannot wait for target: %w", err)
	}
	var code uint32
	if err := windows.GetExitCodeProcess(process.Process, &code); err != nil {
		return 0, fmt.Errorf("cannot read target exit code: %w", err)
	}
	return code, nil
}

func inheritHandle(file *os.File, input bool) (windows.Handle, error) {
	var duplicate windows.Handle
	err := windows.DuplicateHandle(windows.CurrentProcess(), windows.Handle(file.Fd()), windows.CurrentProcess(), &duplicate, 0, true, windows.DUPLICATE_SAME_ACCESS)
	if err == nil {
		return duplicate, nil
	}
	if err != windows.ERROR_INVALID_HANDLE {
		return 0, err
	}
	mode := os.O_WRONLY
	if input {
		mode = os.O_RDONLY
	}
	null, err := os.OpenFile("NUL", mode, 0)
	if err != nil {
		return 0, err
	}
	defer null.Close()
	err = windows.DuplicateHandle(windows.CurrentProcess(), windows.Handle(null.Fd()), windows.CurrentProcess(), &duplicate, 0, true, windows.DUPLICATE_SAME_ACCESS)
	return duplicate, err
}

func lookExecutable(path string, environ map[string]string) (string, error) {
	var candidates []string
	if strings.ContainsAny(path, `/\`) || filepath.IsAbs(path) {
		candidates = []string{path}
	} else {
		candidates = append(candidates, path)
		for _, dir := range filepath.SplitList(envGet(environ, "PATH")) {
			candidates = append(candidates, filepath.Join(dir, path))
		}
	}
	for _, candidate := range candidates {
		names := []string{candidate}
		if filepath.Ext(candidate) == "" {
			names = append(names, candidate+".exe")
		}
		for _, name := range names {
			if info, err := os.Stat(name); err == nil && !info.IsDir() {
				return filepath.Abs(name)
			}
		}
	}
	return "", fmt.Errorf("cannot resolve target executable")
}

func createJob() (windows.Handle, error) {
	job, err := windows.CreateJobObject(nil, nil)
	if err != nil {
		return 0, fmt.Errorf("cannot create job: %w", err)
	}
	limits := windows.JOBOBJECT_EXTENDED_LIMIT_INFORMATION{}
	limits.BasicLimitInformation.LimitFlags = windows.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | windows.JOB_OBJECT_LIMIT_BREAKAWAY_OK
	if _, err := windows.SetInformationJobObject(job, windows.JobObjectExtendedLimitInformation, uintptr(unsafe.Pointer(&limits)), uint32(unsafe.Sizeof(limits))); err != nil {
		windows.CloseHandle(job)
		return 0, fmt.Errorf("cannot configure job: %w", err)
	}
	return job, nil
}

func createSuspended(plan *Plan, startup *windows.StartupInfo, flags uint32, inherit bool) (windows.ProcessInformation, error) {
	var process windows.ProcessInformation
	path := plan.Executable
	application, err := windows.UTF16PtrFromString(path)
	if err != nil {
		return process, fmt.Errorf("invalid executable path")
	}
	commandLine := syscall.EscapeArg(path)
	for _, arg := range plan.Args {
		commandLine += " " + syscall.EscapeArg(arg)
	}
	command, err := windows.UTF16PtrFromString(commandLine)
	if err != nil {
		return process, fmt.Errorf("invalid argument")
	}
	var cwd *uint16
	if plan.CWD != "" {
		cwd, err = windows.UTF16PtrFromString(plan.CWD)
		if err != nil {
			return process, fmt.Errorf("invalid CWD")
		}
	}
	entries := environmentList(plan.Env)
	sort.Slice(entries, func(i, j int) bool { return strings.ToUpper(entries[i]) < strings.ToUpper(entries[j]) })
	block := utf16.Encode([]rune(strings.Join(entries, "\x00") + "\x00\x00"))
	if err := windows.CreateProcess(application, command, nil, nil, inherit, flags, &block[0], cwd, startup, &process); err != nil {
		return process, fmt.Errorf("cannot create target process: %w", err)
	}
	return process, nil
}
