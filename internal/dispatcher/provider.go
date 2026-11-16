package dispatcher

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os/exec"
	"strings"
	"time"
)

const maximumProviderBytes = 16 * 1024 * 1024

type boundedOutput struct{ bytes.Buffer }

func (b *boundedOutput) Write(p []byte) (int, error) {
	if b.Len()+len(p) > maximumProviderBytes {
		return 0, fmt.Errorf("provider output too large")
	}
	return b.Buffer.Write(p)
}

func runProvider(command Command, environ map[string]string) (map[string]string, error) {
	seconds := command.TimeoutSeconds
	if seconds == 0 {
		seconds = 5
	}
	ctx, cancel := context.WithTimeout(context.Background(), time.Duration(seconds)*time.Second)
	defer cancel()
	cmd, err := commandProcess(ctx, command, environ)
	if err != nil {
		return nil, err
	}
	var output boundedOutput
	cmd.Stdout, cmd.Stderr = &output, io.Discard
	if err := cmd.Run(); err != nil {
		if errors.Is(ctx.Err(), context.DeadlineExceeded) {
			return nil, fmt.Errorf("ENV_PROVIDER timed out after %d ms", seconds*1000)
		}
		return nil, fmt.Errorf("ENV_PROVIDER failed") // Never surface provider output as a diagnostic.
	}
	decoder := json.NewDecoder(bytes.NewReader(output.Bytes()))
	token, err := decoder.Token()
	if err != nil || token != json.Delim('{') {
		return nil, fmt.Errorf("ENV_PROVIDER must return a JSON object of strings")
	}
	values := map[string]string{}
	for decoder.More() {
		token, err := decoder.Token()
		if err != nil {
			return nil, fmt.Errorf("invalid ENV_PROVIDER JSON object")
		}
		name, ok := token.(string)
		var value *string
		if !ok || decoder.Decode(&value) != nil || value == nil {
			return nil, fmt.Errorf("ENV_PROVIDER must return a JSON object of strings")
		}
		if err := validateEnv(name, *value); err != nil {
			return nil, err
		}
		envSet(values, name, *value)
	}
	if _, err := decoder.Token(); err != nil {
		return nil, fmt.Errorf("invalid ENV_PROVIDER JSON object")
	}
	if _, err := decoder.Token(); err != io.EOF {
		return nil, fmt.Errorf("invalid trailing ENV_PROVIDER output")
	}
	return values, nil
}

func commandProcess(ctx context.Context, command Command, environ map[string]string) (*exec.Cmd, error) {
	executable, arguments := command.Command, command.Args
	if command.raw != "" {
		var err error
		executable, err = rawExecutable(command.raw)
		if err != nil {
			return nil, err
		}
	}
	if executable == "" {
		return nil, fmt.Errorf("command requires an executable")
	}
	cmd := exec.CommandContext(ctx, executable, arguments...)
	configureCommand(cmd, command.raw)
	cmd.Dir, cmd.Env = command.CWD, environmentList(environ)
	return cmd, nil
}

func rawExecutable(command string) (string, error) {
	command = strings.TrimSpace(command)
	if command == "" {
		return "", fmt.Errorf("command requires an executable")
	}
	if command[0] == '"' {
		end := strings.IndexByte(command[1:], '"')
		if end < 0 {
			return "", fmt.Errorf("command has an unclosed executable quote")
		}
		return command[1 : end+1], nil
	}
	end := strings.IndexAny(command, " \t")
	if end < 0 {
		return command, nil
	}
	return command[:end], nil
}

func environmentList(environ map[string]string) []string {
	var entries []string
	for _, key := range sortedKeys(environ) {
		entries = append(entries, key+"="+environ[key])
	}
	return entries
}
