//go:build !windows

package dispatcher

import (
	"fmt"
	"os/exec"
)

func configureCommand(cmd *exec.Cmd, raw string) {}
func Run(plan *Plan) (uint32, error)             { return 0, fmt.Errorf("dispatcher targets Windows only") }

func PackageHelper(pipeName, executable string, parentPID uint32) error {
	return fmt.Errorf("dispatcher targets Windows only")
}
