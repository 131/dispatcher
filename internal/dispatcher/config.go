package dispatcher

import (
	"bytes"
	"encoding/xml"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"gopkg.in/yaml.v3"
)

// Config keeps the XML compatibility layer separate from the typed Go syntax.
type Config struct {
	Path           string            `yaml:"path"`
	AppName        string            `yaml:"app_name"`
	PathFlavors    map[string]string `yaml:"path_flavors"`
	Args           []string          `yaml:"args"`
	Env            map[string]string `yaml:"env"`
	CWD            string            `yaml:"cwd"`
	Output         string            `yaml:"output"`
	Provider       *Command          `yaml:"env_provider"`
	Prestart       *Command          `yaml:"prestart"`
	ShowWindow     *bool             `yaml:"use_showwindow"`
	UseJob         *bool             `yaml:"use_job"`
	Detached       bool              `yaml:"detached"`
	AsService      any               `yaml:"as_service"`
	AsDesktopUser  any               `yaml:"as_desktop_user"`
	AsUser         bool              `yaml:"as_user"`
	Username       string            `yaml:"username"`
	Password       string            `yaml:"password"`
	UWFDisabled    bool              `yaml:"uwf_servicing_disabled"`
	UWFDetect      bool              `yaml:"uwf_servicing_detect"`
	RestartNetwork bool              `yaml:"service_restart_on_network_change"`
	Vauth          yaml.Node         `yaml:"vauth"`
	XML            map[string]string `yaml:"-"`
	inlineRC       []byte
}

type Command struct {
	Command        string   `yaml:"command"`
	Args           []string `yaml:"args"`
	CWD            string   `yaml:"cwd"`
	TimeoutSeconds int      `yaml:"timeout_seconds"`
	raw            string
}

// Load retains XML file precedence. A principal YAML file is complete, not a fragment overlay.
func Load(executable string) (*Config, error) {
	dir := filepath.Dir(executable)
	name := strings.TrimSuffix(filepath.Base(executable), filepath.Ext(executable))
	config := &Config{XML: map[string]string{}}
	found := false
	for _, file := range []string{filepath.Join(dir, name+".config"), filepath.Join(dir, name+".exe.config")} {
		body, err := os.ReadFile(file)
		if os.IsNotExist(err) {
			continue
		}
		if err != nil {
			return nil, fmt.Errorf("cannot read %s", filepath.Base(file))
		}
		found = true
		if !isXML(body) {
			return parseYAML(body)
		}
		if err := mergeXML(config, body, false); err != nil {
			return nil, fmt.Errorf("%s: %w", filepath.Base(file), err)
		}
	}
	fragmentDir := filepath.Join(dir, name+".config.d")
	entries, err := os.ReadDir(fragmentDir)
	if err != nil && !os.IsNotExist(err) {
		return nil, fmt.Errorf("cannot read configuration fragments")
	}
	for _, entry := range entries {
		if entry.IsDir() || !strings.EqualFold(filepath.Ext(entry.Name()), ".config") {
			continue
		}
		found = true
		body, err := os.ReadFile(filepath.Join(fragmentDir, entry.Name()))
		if err != nil {
			return nil, fmt.Errorf("cannot read fragment %s", entry.Name())
		}
		if !isXML(body) {
			return nil, fmt.Errorf("fragment %s must contain XML", entry.Name())
		}
		if err := mergeXML(config, body, true); err != nil {
			return nil, fmt.Errorf("fragment %s: %w", entry.Name(), err)
		}
	}
	if !found {
		return nil, fmt.Errorf("cannot resolve configuration")
	}
	return config, nil
}

func isXML(body []byte) bool {
	body = bytes.TrimPrefix(body, []byte{0xef, 0xbb, 0xbf})
	return bytes.HasPrefix(bytes.TrimSpace(body), []byte("<?"))
}

func mergeXML(config *Config, body []byte, fragment bool) error {
	var document struct {
		XMLName xml.Name `xml:"configuration"`
		Adds    []struct {
			Key   *string `xml:"key,attr"`
			Value *string `xml:"value,attr"`
		} `xml:"appSettings>add"`
		Vauth []struct {
			Body string `xml:",chardata"`
		} `xml:"vauth"`
	}
	decoder := xml.NewDecoder(bytes.NewReader(body))
	if err := decoder.Decode(&document); err != nil {
		return fmt.Errorf("invalid XML configuration")
	}
	for {
		token, err := decoder.Token()
		if err == io.EOF {
			break
		}
		if err != nil {
			return fmt.Errorf("invalid XML configuration")
		}
		switch token := token.(type) {
		case xml.Comment:
		case xml.ProcInst:
			if token.Target == "xml" {
				return fmt.Errorf("invalid XML configuration")
			}
		case xml.CharData:
			if len(bytes.TrimSpace(token)) != 0 {
				return fmt.Errorf("invalid XML configuration")
			}
		default:
			return fmt.Errorf("invalid XML configuration")
		}
	}
	for _, add := range document.Adds {
		if add.Key != nil && add.Value != nil {
			config.XML[*add.Key] = *add.Value
		}
	}
	if len(document.Vauth) != 0 {
		if fragment || len(document.Vauth) != 1 || config.inlineRC != nil {
			return fmt.Errorf("vauth must occur once in the principal configuration")
		}
		config.inlineRC = []byte(document.Vauth[0].Body)
	}
	return nil
}

func parseYAML(body []byte) (*Config, error) {
	var document yaml.Node
	decoder := yaml.NewDecoder(bytes.NewReader(body))
	if err := decoder.Decode(&document); err != nil {
		return nil, fmt.Errorf("invalid YAML configuration")
	}
	var extra yaml.Node
	if err := decoder.Decode(&extra); err != io.EOF {
		return nil, fmt.Errorf("expected one YAML document")
	}
	if len(document.Content) != 1 || document.Content[0].Kind != yaml.MappingNode {
		return nil, fmt.Errorf("expected a YAML mapping")
	}
	root := document.Content[0]
	if err := validateYAML(root); err != nil {
		return nil, err
	}
	for i := 0; i < len(root.Content); i += 2 {
		key, value := root.Content[i].Value, root.Content[i+1]
		if err := validateField(key, value); err != nil {
			return nil, err
		}
	}
	var config Config
	strict := yaml.NewDecoder(bytes.NewReader(body))
	strict.KnownFields(true)
	if err := strict.Decode(&config); err != nil {
		return nil, fmt.Errorf("invalid YAML configuration field or type")
	}
	if config.Vauth.Kind != 0 {
		rc, err := yaml.Marshal(&config.Vauth)
		if err != nil {
			return nil, fmt.Errorf("invalid vauth section")
		}
		config.inlineRC = rc
	}
	return &config, nil
}

func validateYAML(node *yaml.Node) error {
	if node.Kind == yaml.AliasNode || node.Tag == "!!merge" || (!strings.HasPrefix(node.Tag, "!!") && node.Tag != "") {
		return fmt.Errorf("unsupported YAML alias, merge or tag")
	}
	if node.Kind == yaml.MappingNode {
		keys := map[string]bool{}
		for i := 0; i < len(node.Content); i += 2 {
			key := node.Content[i]
			if key.Tag != "!!str" || keys[key.Value] {
				return fmt.Errorf("invalid or duplicate YAML mapping key")
			}
			keys[key.Value] = true
		}
	}
	for _, child := range node.Content {
		if err := validateYAML(child); err != nil {
			return err
		}
	}
	return nil
}

func validateField(key string, value *yaml.Node) error {
	tag := ""
	switch key {
	case "path", "app_name", "cwd", "output", "username", "password":
		tag = "!!str"
	case "args":
		if value.Kind != yaml.SequenceNode {
			return fmt.Errorf("args must be a list of strings")
		}
		for _, item := range value.Content {
			if item.Tag != "!!str" {
				return fmt.Errorf("args must be a list of strings")
			}
		}
		return nil
	case "env", "path_flavors":
		if value.Kind != yaml.MappingNode {
			return fmt.Errorf("%s must be a string mapping", key)
		}
		seen := map[string]bool{}
		for i := 0; i < len(value.Content); i += 2 {
			name, item := value.Content[i].Value, value.Content[i+1]
			if item.Tag != "!!str" {
				return fmt.Errorf("%s values must be strings", key)
			}
			if key == "env" && seen[strings.ToUpper(name)] {
				return fmt.Errorf("duplicate environment name")
			}
			seen[strings.ToUpper(name)] = true
		}
		return nil
	case "use_showwindow", "use_job", "detached", "as_user", "uwf_servicing_disabled", "uwf_servicing_detect", "service_restart_on_network_change":
		tag = "!!bool"
	case "as_service", "as_desktop_user":
		if value.Tag == "!!bool" || (value.Tag == "!!str" && value.Value == "auto") {
			return nil
		}
		return fmt.Errorf("%s must be boolean or auto", key)
	case "env_provider", "prestart", "vauth":
		if value.Kind != yaml.MappingNode {
			return fmt.Errorf("%s must be a mapping", key)
		}
		if key != "vauth" {
			for i := 0; i < len(value.Content); i += 2 {
				name, item := value.Content[i].Value, value.Content[i+1]
				switch name {
				case "command":
					if item.Tag != "!!str" || item.Value == "" {
						return fmt.Errorf("command must be a nonempty string")
					}
				case "args":
					if err := validateField("args", item); err != nil {
						return err
					}
				case "cwd":
					if key != "env_provider" || item.Tag != "!!str" {
						return fmt.Errorf("invalid command cwd")
					}
				case "timeout_seconds":
					var seconds int
					if key != "env_provider" || item.Tag != "!!int" || item.Decode(&seconds) != nil || seconds <= 0 || seconds > 2147483 {
						return fmt.Errorf("invalid provider timeout")
					}
				default:
					return fmt.Errorf("unknown command field")
				}
			}
		}
		return nil
	}
	if tag != "" && value.Tag != tag {
		return fmt.Errorf("invalid type for %s", key)
	}
	return nil
}

func boolValue(value string) (bool, error) {
	switch strings.ToLower(value) {
	case "", "false", "0":
		return false, nil
	case "true", "1":
		return true, nil
	default:
		return false, fmt.Errorf("expected true, false, 1, 0 or empty")
	}
}

func sortedKeys(values map[string]string) []string {
	keys := make([]string, 0, len(values))
	for key := range values {
		keys = append(keys, key)
	}
	sort.Strings(keys)
	return keys
}
