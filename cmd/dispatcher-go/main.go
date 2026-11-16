package main

import (
	"fmt"
	"os"
	"os/signal"
	"strconv"

	"github.com/131/dispatcher/v4/internal/dispatcher"
)

var gui = "false"

func main() {
	code, err := run()
	if err != nil {
		fmt.Fprintln(os.Stderr, "dispatcher:", err)
		os.Exit(1)
	}
	os.Exit(int(code))
}

func run() (uint32, error) {
	// The attached child receives console Ctrl+C; keep its waiting dispatcher alive.
	interrupts := make(chan os.Signal, 1)
	signal.Notify(interrupts, os.Interrupt)
	defer signal.Stop(interrupts)
	if len(os.Args) == 5 && os.Args[1] == "--dispatcher-go-package-helper" {
		parent, err := strconv.ParseUint(os.Args[4], 10, 32)
		if err != nil || parent == 0 {
			return 0, fmt.Errorf("invalid package helper parent")
		}
		return 0, dispatcher.PackageHelper(os.Args[2], os.Args[3], uint32(parent))
	}
	executable, err := os.Executable()
	if err != nil {
		return 0, err
	}
	config, err := dispatcher.Load(executable)
	if err != nil {
		return 0, err
	}
	plan, err := dispatcher.Prepare(config, executable, os.Args[1:], gui == "true")
	if err != nil {
		return 0, err
	}
	return dispatcher.Run(plan)
}
