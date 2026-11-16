package dispatcher

import (
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	vvauth "github.com/131/vvauth/v2"
)

type Plan struct {
	Executable string
	AppName    string
	Args       []string
	Env        map[string]string
	CWD        string
	Output     string
	ShowWindow bool
	UseJob     bool
	Detached   bool
	Prestart   *Command
}

func Prepare(config *Config, executable string, arguments []string, gui bool) (*Plan, error) {
	environ := map[string]string{}
	for _, entry := range os.Environ() {
		if key, value, ok := strings.Cut(entry, "="); ok && key != "" {
			envSet(environ, key, value)
		}
	}
	return prepare(config, executable, arguments, gui, environ, time.Now())
}

func prepare(config *Config, executable string, arguments []string, gui bool, inherited map[string]string, now time.Time) (*Plan, error) {
	local := *config
	local.Args = append([]string(nil), config.Args...)
	config = &local
	dir := filepath.Dir(executable)
	if config.XML != nil {
		if err := normalizeXML(config, executable, inherited); err != nil {
			return nil, err
		}
	}
	if config.AppName != "" && (config.Path != "" || len(config.PathFlavors) != 0) {
		return nil, fmt.Errorf("specify APP_NAME or PATH, not both")
	}
	if config.AppName == "" && config.Path == "" && len(config.PathFlavors) == 0 {
		return nil, fmt.Errorf("cannot resolve target")
	}
	path := config.Path
	name := strings.TrimSuffix(filepath.Base(executable), filepath.Ext(executable))
	if flavor := envGet(inherited, "DISPATCHER_"+name+"_FLAVOR"); config.AppName == "" && flavor != "" {
		var ok bool
		path, ok = config.PathFlavors[flavor]
		if !ok {
			return nil, fmt.Errorf("cannot resolve selected flavor")
		}
	}
	if config.AppName == "" && path == "" {
		return nil, fmt.Errorf("target executable must not be empty")
	}
	// Reject unported identity/service modes instead of silently degrading them.
	if config.AppName != "" && (modeEnabled(config.AsService) || modeEnabled(config.AsDesktopUser) || config.AsUser || config.RestartNetwork) {
		return nil, fmt.Errorf("APP_NAME supports the interactive Windows user only; services, alternate users and network restart are not supported")
	}
	if modeEnabled(config.AsService) || modeEnabled(config.AsDesktopUser) || config.AsUser || config.UWFDisabled || config.UWFDetect || config.RestartNetwork {
		return nil, fmt.Errorf("service, alternate-user and UWF modes are not implemented in the Go increment yet")
	}
	appName := config.AppName
	if appName != "" {
		appName = expand(appName, inherited, nil)
		if !strings.Contains(appName, "%") {
			if err := validateAppName(appName); err != nil {
				return nil, err
			}
		}
	}
	if config.Provider != nil && config.inlineRC != nil {
		return nil, fmt.Errorf("vauth and ENV_PROVIDER are mutually exclusive")
	}
	if config.Provider != nil {
		if config.Provider.Command == "" {
			return nil, fmt.Errorf("ENV_PROVIDER requires an executable")
		}
		if config.Provider.TimeoutSeconds < 0 || config.Provider.TimeoutSeconds > 2147483 {
			return nil, fmt.Errorf("provider timeout must be positive seconds within the XML range")
		}
	}
	environ := cloneEnv(inherited)
	macros := map[string]string{"dwd": dir, "Y": now.Format("2006"), "m": now.Format("01"), "d": now.Format("02"), "H": now.Format("15"), "i": now.Format("04"), "s": now.Format("05")}
	explicit := map[string]string{}
	for _, key := range sortedKeys(config.Env) {
		value := expand(config.Env[key], inherited, macros)
		if err := validateEnv(key, value); err != nil {
			return nil, err
		}
		envSet(explicit, key, value)
		envSet(environ, key, value)
	}
	if config.inlineRC != nil {
		client, err := vvauth.NewFromRC(config.inlineRC, environ)
		if err != nil {
			return nil, fmt.Errorf("vauth configuration: %w", err)
		}
		exports, err := client.Env(false)
		if err != nil {
			return nil, fmt.Errorf("vauth environment: %w", err)
		}
		for _, key := range exports.Keys {
			value := exports.Values[key]
			if err := validateEnv(key, value); err != nil {
				return nil, err
			}
			envSet(environ, key, value)
		}
	} else if config.Provider != nil {
		providerFields := environ
		if config.XML != nil {
			providerFields = inherited
		}
		provider := expandCommand(*config.Provider, providerFields, macros)
		values, err := runProvider(provider, environ)
		if err != nil {
			return nil, err
		}
		for key, value := range values {
			envSet(environ, key, value)
		}
	}
	for key, value := range explicit {
		envSet(environ, key, value)
	}
	fieldEnv := environ
	if config.XML != nil {
		fieldEnv = inherited
	} // Legacy XML expands against the dispatcher's original process environment.
	if config.AppName != "" {
		appName = expand(config.AppName, fieldEnv, macros)
		if err := validateAppName(appName); err != nil {
			return nil, err
		}
	}
	path = expand(path, fieldEnv, macros)
	if config.AppName == "" && path == "" {
		return nil, fmt.Errorf("target executable must not be empty")
	}
	candidate := path
	if config.AppName == "" && !filepath.IsAbs(candidate) {
		candidate = filepath.Join(dir, candidate)
	}
	if info, err := os.Stat(candidate); config.AppName == "" && err == nil && !info.IsDir() {
		path = candidate
	}
	plan := &Plan{Executable: path, AppName: appName, Env: environ, CWD: expand(config.CWD, fieldEnv, macros), Output: expand(config.Output, fieldEnv, macros), ShowWindow: !gui, UseJob: true, Detached: config.Detached}
	if config.ShowWindow != nil {
		plan.ShowWindow = *config.ShowWindow
	}
	if config.UseJob != nil {
		plan.UseJob = *config.UseJob
	}
	for _, arg := range config.Args {
		plan.Args = append(plan.Args, expand(arg, fieldEnv, macros))
	}
	plan.Args = append(plan.Args, arguments...)
	if config.Prestart != nil {
		command := expandCommand(*config.Prestart, fieldEnv, macros)
		command.CWD = plan.CWD
		plan.Prestart = &command
	}
	if config.AppName == "" && filepath.IsAbs(path) {
		envSet(environ, "PATH", envGet(environ, "PATH")+string(os.PathListSeparator)+filepath.Dir(path))
	}
	return plan, nil
}

func modeEnabled(value any) bool {
	if flag, ok := value.(bool); ok {
		return flag
	}
	return value == "auto" // Until service detection is ported, auto is not silently treated as interactive.
}

func normalizeXML(config *Config, executable string, environ map[string]string) error {
	settings := config.XML
	name := strings.TrimSuffix(filepath.Base(executable), filepath.Ext(executable))
	flavor := envGet(environ, "DISPATCHER_"+name+"_FLAVOR")
	pathKey := "PATH"
	if flavor != "" {
		pathKey += "_" + flavor
	}
	_, hasApp := settings["APP_NAME"]
	_, hasPath := settings["PATH"]
	_, hasFlavor := settings[pathKey]
	if hasApp && (hasPath || hasFlavor) {
		return fmt.Errorf("specify APP_NAME or PATH, not both")
	}
	if hasApp {
		if settings["APP_NAME"] == "" {
			return fmt.Errorf("APP_NAME must not be empty")
		}
		config.AppName = settings["APP_NAME"]
	} else if !hasFlavor {
		return fmt.Errorf("cannot resolve selected PATH")
	}
	config.Path = settings["PATH"]
	config.Env = map[string]string{}
	config.PathFlavors = map[string]string{}
	config.Username, config.Password = envGet(environ, "DISPATCHER_USERNAME"), envGet(environ, "DISPATCHER_PASSWORD")
	for _, key := range sortedKeys(settings) {
		value := settings[key]
		switch {
		case strings.HasPrefix(key, "PATH_"):
			config.PathFlavors[strings.TrimPrefix(key, "PATH_")] = value
		case strings.HasPrefix(key, "ARGV"):
			config.Args = append(config.Args, value)
		case strings.HasPrefix(key, "ENV_") && !strings.HasPrefix(key, "ENV_PROVIDER"):
			config.Env[strings.TrimPrefix(key, "ENV_")] = value
		}
		switch key {
		case "CWD":
			config.CWD = value
		case "OUTPUT":
			config.Output = value
		case "USERNAME":
			config.Username = value
		case "PASSWORD":
			config.Password = value
		case "ENV_PROVIDER":
			config.Provider = &Command{Command: value, raw: value}
		case "PRESTART_CMD":
			config.Prestart = &Command{Command: value, raw: value}
		case "AS_SERVICE", "AS_DESKTOP_USER":
			var mode any = "auto"
			if value != "auto" {
				flag, err := boolValue(value)
				if err != nil {
					return fmt.Errorf("%s: %w", key, err)
				}
				mode = flag
			}
			if key == "AS_SERVICE" {
				config.AsService = mode
			} else {
				config.AsDesktopUser = mode
			}
		case "USE_SHOWWINDOW", "USE_JOB", "DETACHED", "AS_USER", "UWF_SERVICING_DISABLED", "UWF_SERVICING_DETECT", "SERVICE_RESTART_ON_NETWORK_CHANGE":
			flag, err := boolValue(value)
			if err != nil {
				return fmt.Errorf("%s: %w", key, err)
			}
			switch key {
			case "USE_SHOWWINDOW":
				config.ShowWindow = &flag
			case "USE_JOB":
				config.UseJob = &flag
			case "DETACHED":
				config.Detached = flag
			case "AS_USER":
				config.AsUser = flag
			case "UWF_SERVICING_DISABLED":
				config.UWFDisabled = flag
			case "UWF_SERVICING_DETECT":
				config.UWFDetect = flag
			case "SERVICE_RESTART_ON_NETWORK_CHANGE":
				config.RestartNetwork = flag
			}
		}
	}
	if config.Provider != nil {
		config.Provider.CWD = settings["ENV_PROVIDER_CWD"]
		if value, exists := settings["ENV_PROVIDER_TIMEOUT"]; exists {
			seconds, err := strconv.Atoi(value)
			if err != nil || seconds <= 0 || seconds > 2147483 {
				return fmt.Errorf("ENV_PROVIDER_TIMEOUT must be a positive number of seconds")
			}
			config.Provider.TimeoutSeconds = seconds
		}
	}
	return nil
}

func expandCommand(command Command, environ, macros map[string]string) Command {
	command.Args = append([]string(nil), command.Args...)
	command.Command = expand(command.Command, environ, macros)
	command.raw = expand(command.raw, environ, macros)
	command.CWD = expand(command.CWD, environ, macros)
	for i, value := range command.Args {
		command.Args[i] = expand(value, environ, macros)
	}
	return command
}

func expand(value string, environ, macros map[string]string) string {
	for key, replacement := range macros {
		value = strings.ReplaceAll(value, "%"+key+"%", replacement)
	}
	var out strings.Builder
	for len(value) > 0 {
		start := strings.IndexByte(value, '%')
		if start < 0 {
			out.WriteString(value)
			break
		}
		out.WriteString(value[:start])
		value = value[start:]
		end := strings.IndexByte(value[1:], '%')
		if end < 0 {
			out.WriteString(value)
			break
		}
		end += 1
		name := value[1:end]
		found := false
		for key, replacement := range environ {
			if strings.EqualFold(key, name) {
				out.WriteString(replacement)
				found = true
				break
			}
		}
		if !found {
			out.WriteString(value[:end+1])
		}
		value = value[end+1:]
	}
	return out.String()
}

func envGet(environ map[string]string, key string) string {
	for name, value := range environ {
		if strings.EqualFold(name, key) {
			return value
		}
	}
	return ""
}

func envSet(environ map[string]string, key, value string) {
	for name := range environ {
		if name != key && strings.EqualFold(name, key) {
			delete(environ, name)
		}
	}
	environ[key] = value
}

func cloneEnv(environ map[string]string) map[string]string {
	copy := map[string]string{}
	for name, value := range environ {
		envSet(copy, name, value)
	}
	return copy
}

func validateEnv(key, value string) error {
	if key == "" || strings.ContainsAny(key, "=\x00") || strings.ContainsRune(value, 0) {
		return fmt.Errorf("invalid environment entry")
	}
	return nil
}
