//go:build windows

package dispatcher

import (
	"fmt"
	"os"
	"runtime"
	"strconv"
	"strings"
	"syscall"
	"unsafe"

	"golang.org/x/sys/windows"
)

var packageKernel = windows.NewLazySystemDLL("kernel32.dll")
var getPackagesByFamily = packageKernel.NewProc("GetPackagesByPackageFamily")
var getPackagePath = packageKernel.NewProc("GetPackagePathByFullName")
var attachConsole = packageKernel.NewProc("AttachConsole")
var packageOle = windows.NewLazySystemDLL("ole32.dll")
var coInitialize = packageOle.NewProc("CoInitializeEx")
var coUninitialize = packageOle.NewProc("CoUninitialize")
var coCreateInstance = packageOle.NewProc("CoCreateInstance")

func resolvePackageExecutable(appName string) (string, error) {
	if err := validateAppName(appName); err != nil {
		return "", err
	}
	family, _, _ := strings.Cut(appName, "!")
	familyPtr, err := windows.UTF16PtrFromString(family)
	if err != nil {
		return "", fmt.Errorf("invalid APP_NAME family")
	}
	var count, length uint32
	code, _, _ := getPackagesByFamily.Call(uintptr(unsafe.Pointer(familyPtr)), uintptr(unsafe.Pointer(&count)), 0, uintptr(unsafe.Pointer(&length)), 0)
	if code != 0 && code != uintptr(windows.ERROR_INSUFFICIENT_BUFFER) {
		return "", fmt.Errorf("cannot enumerate APP_NAME packages: %w", syscall.Errno(code))
	}
	if count == 0 {
		return "", fmt.Errorf("APP_NAME package is not installed for this Windows user")
	}
	names, buffer := make([]*uint16, count), make([]uint16, length)
	code, _, _ = getPackagesByFamily.Call(uintptr(unsafe.Pointer(familyPtr)), uintptr(unsafe.Pointer(&count)), uintptr(unsafe.Pointer(&names[0])), uintptr(unsafe.Pointer(&length)), uintptr(unsafe.Pointer(&buffer[0])))
	if code != 0 {
		return "", fmt.Errorf("cannot enumerate APP_NAME packages: %w", syscall.Errno(code))
	}
	var directories []string
	for _, name := range names[:count] {
		directory, err := registeredPackagePath(name)
		if err != nil {
			return "", err
		}
		directories = append(directories, directory)
	}
	runtime.KeepAlive(buffer)
	return selectPackageExecutable(appName, directories)
}

func registeredPackagePath(name *uint16) (string, error) {
	var length uint32
	code, _, _ := getPackagePath.Call(uintptr(unsafe.Pointer(name)), uintptr(unsafe.Pointer(&length)), 0)
	if code != uintptr(windows.ERROR_INSUFFICIENT_BUFFER) {
		return "", fmt.Errorf("cannot resolve APP_NAME package path: %w", syscall.Errno(code))
	}
	path := make([]uint16, length)
	code, _, _ = getPackagePath.Call(uintptr(unsafe.Pointer(name)), uintptr(unsafe.Pointer(&length)), uintptr(unsafe.Pointer(&path[0])))
	if code != 0 {
		return "", fmt.Errorf("cannot resolve APP_NAME package path: %w", syscall.Errno(code))
	}
	return windows.UTF16ToString(path), nil
}

// Keep the existing internal DesktopAppX COM ABI isolated from process/config logic.
type desktopActivator struct{ vtable *desktopActivatorVtable }
type desktopActivatorVtable struct{ QueryInterface, AddRef, Release, Activate, ActivateWithOptions uintptr }

func startInPackage(appName, executable string, arguments []string) (windows.Handle, error) {
	if err := validateAppName(appName); err != nil {
		return 0, err
	}
	runtime.LockOSThread()
	defer runtime.UnlockOSThread()
	hresult, _, _ := coInitialize.Call(0, 0)
	if int32(hresult) < 0 {
		return 0, fmt.Errorf("cannot initialize package COM (HRESULT 0x%08X)", uint32(hresult))
	}
	defer coUninitialize.Call()
	class, _ := windows.GUIDFromString("{168EB462-775F-42AE-9111-D714B2306C2E}")
	iface, _ := windows.GUIDFromString("{F158268A-D5A5-45CE-99CF-00D6C3F3FC0A}")
	var activator *desktopActivator
	hresult, _, _ = coCreateInstance.Call(uintptr(unsafe.Pointer(&class)), 0, 5, uintptr(unsafe.Pointer(&iface)), uintptr(unsafe.Pointer(&activator)))
	if int32(hresult) < 0 {
		return 0, fmt.Errorf("cannot create package activator (HRESULT 0x%08X)", uint32(hresult))
	}
	defer syscall.SyscallN(activator.vtable.Release, uintptr(unsafe.Pointer(activator)))
	appPtr, err := windows.UTF16PtrFromString(appName)
	if err != nil {
		return 0, fmt.Errorf("invalid APP_NAME")
	}
	exePtr, err := windows.UTF16PtrFromString(executable)
	if err != nil {
		return 0, fmt.Errorf("invalid package helper path")
	}
	var encoded []string
	for _, arg := range arguments {
		encoded = append(encoded, syscall.EscapeArg(arg))
	}
	argsPtr, err := windows.UTF16PtrFromString(strings.Join(encoded, " "))
	if err != nil {
		return 0, fmt.Errorf("invalid package helper arguments")
	}
	var process windows.Handle
	// Same PreventBreakaway/update/Centennial flags as the C# bootstrap.
	hresult, _, _ = syscall.SyscallN(activator.vtable.ActivateWithOptions, uintptr(unsafe.Pointer(activator)), uintptr(unsafe.Pointer(appPtr)), uintptr(unsafe.Pointer(exePtr)), uintptr(unsafe.Pointer(argsPtr)), 4|16|32, 0, uintptr(unsafe.Pointer(&process)))
	if process != 0 {
		defer windows.CloseHandle(process)
	}
	if int32(hresult) < 0 {
		return 0, fmt.Errorf("native APP_NAME activation failed (HRESULT 0x%08X)", uint32(hresult))
	}
	pid, err := windows.GetProcessId(process)
	if err != nil {
		return 0, fmt.Errorf("cannot identify package helper: %w", err)
	}
	return openPackageHelper(pid)
}

func openPackageHelper(pid uint32) (windows.Handle, error) {
	return windows.OpenProcess(windows.PROCESS_DUP_HANDLE|windows.PROCESS_SET_QUOTA|windows.PROCESS_TERMINATE|windows.SYNCHRONIZE|windows.PROCESS_QUERY_LIMITED_INFORMATION, false, pid)
}

func runAppPackage(plan *Plan) (uint32, error) {
	target, err := resolvePackageExecutable(plan.AppName)
	if err != nil {
		return 0, err
	}
	local := *plan
	local.Executable = target
	if err := runPrestart(&local); err != nil {
		return 0, err
	}
	self, err := os.Executable()
	if err != nil {
		return 0, err
	}
	return runPackage(&local, func(pipe string) (windows.Handle, error) {
		return startInPackage(plan.AppName, self, []string{"--dispatcher-go-package-helper", pipe, target, strconv.Itoa(os.Getpid())})
	}, packageTimeout)
}
