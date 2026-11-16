package dispatcher

import (
	"fmt"
	"os"
	"path/filepath"
	"reflect"
	"testing"
	"time"
)

const xmlStart = `<?xml version="1.0"?><configuration><appSettings>`
const xmlEnd = `</appSettings></configuration>`

func writeConfig(t *testing.T, path, body string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0700); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(body), 0600); err != nil {
		t.Fatal(err)
	}
}

func TestXMLPrecedenceAndFragmentOrder(t *testing.T) {
	exe := filepath.Join(t.TempDir(), "tool.exe")
	writeConfig(t, exe[:len(exe)-4]+".config", xmlStart+`<add key="PATH" value="first.exe"/>`+xmlEnd)
	writeConfig(t, exe+".config", xmlStart+`<add key="PATH" value="second.exe"/>`+xmlEnd)
	fragments := exe[:len(exe)-4] + ".config.d"
	writeConfig(t, filepath.Join(fragments, "z.config"), xmlStart+`<add key="PATH" value="last.exe"/>`+xmlEnd)
	writeConfig(t, filepath.Join(fragments, "a.config"), xmlStart+`<add key="PATH" value="earlier.exe"/>`+xmlEnd)
	writeConfig(t, filepath.Join(fragments, "ignored.yaml"), "invalid")
	config, err := Load(exe)
	if err != nil {
		t.Fatal(err)
	}
	if config.XML["PATH"] != "last.exe" {
		t.Fatal("fragment order/precedence changed")
	}
}

func TestOnlyConfigExtensionAndContentDetection(t *testing.T) {
	for _, extension := range []string{".yaml", ".yml"} {
		exe := filepath.Join(t.TempDir(), "tool.exe")
		writeConfig(t, exe[:len(exe)-4]+extension, "path: child.exe")
		if _, err := Load(exe); err == nil {
			t.Fatal("discovered forbidden extension")
		}
	}
	for _, body := range []string{"path: child.exe", "\ufeff  \n" + xmlStart + `<add key="PATH" value="child.exe"/>` + xmlEnd} {
		exe := filepath.Join(t.TempDir(), "tool.exe")
		writeConfig(t, exe[:len(exe)-4]+".config", body)
		if _, err := Load(exe); err != nil {
			t.Fatal(err)
		}
	}
	exe := filepath.Join(t.TempDir(), "tool.exe")
	writeConfig(t, exe+".config", "path: child.exe")
	writeConfig(t, exe[:len(exe)-4]+".config.d/broken.config", "not YAML fragments")
	if _, err := Load(exe); err != nil {
		t.Fatal("YAML configuration must not load fragments", err)
	}
}

func TestInvalidConfigurationsFail(t *testing.T) {
	for _, body := range []string{
		"<?xml bad", xmlStart + xmlEnd + "<second/>", "path: [", "- item",
		"path: child.exe\npath: duplicate.exe", "path: child.exe\nversion: 1",
		"path: child.exe\nargs: [1]", "path: child.exe\nenv: {FLAG: true}",
		"path: child.exe\nenv: {PATH: one, Path: two}", "path: child.exe\nuse_job: 'false'",
		"path: child.exe\nuse_job: null", "path: child.exe\n---\npath: other.exe",
		"path: child.exe\nenv_provider: {command: provider, timeout_seconds: 0}",
		"path: child.exe\nenv_provider: {command: provider, timeout_seconds: null}",
		"path: child.exe\nenv_provider: {command: provider, args: [false]}",
		"path: child.exe\nprestart: {command: provider, timeout_seconds: 1}",
	} {
		exe := filepath.Join(t.TempDir(), "tool.exe")
		writeConfig(t, exe+".config", body)
		if _, err := Load(exe); err == nil {
			t.Fatalf("invalid config accepted: %q", body)
		}
	}
	exe := filepath.Join(t.TempDir(), "tool.exe")
	writeConfig(t, exe+".config", xmlStart+`<add key="PATH" value="child.exe"/>`+xmlEnd)
	writeConfig(t, exe[:len(exe)-4]+".config.d/a.config", "path: child.exe")
	if _, err := Load(exe); err == nil {
		t.Fatal("YAML fragment accepted")
	}
}

func TestBooleanContract(t *testing.T) {
	for _, value := range []string{"", "false", "False", "FALSE", "0", "true", "True", "TRUE", "1"} {
		flag, err := boolValue(value)
		if err != nil {
			t.Fatal(err)
		}
		want := value == "true" || value == "True" || value == "TRUE" || value == "1"
		if flag != want {
			t.Fatalf("wrong boolean %q", value)
		}
	}
	for _, value := range []string{"yes", "no", " true ", "2"} {
		if _, err := boolValue(value); err == nil {
			t.Fatal("arbitrary boolean accepted")
		}
	}
}

func TestXMLArgumentOrderAndFalseRestart(t *testing.T) {
	config := &Config{XML: map[string]string{"PATH": "child.exe", "ARGV2": "two", "ARGV10": "ten", "ARGV0": "", "SERVICE_RESTART_ON_NETWORK_CHANGE": "False"}}
	for i := 0; i < 2; i++ {
		plan, err := prepare(config, filepath.Join(t.TempDir(), "tool.exe"), []string{"cli"}, false, map[string]string{}, time.Now())
		if err != nil {
			t.Fatal(err)
		}
		if !reflect.DeepEqual(plan.Args, []string{"", "ten", "two", "cli"}) {
			t.Fatalf("wrong argv order: %#v", plan.Args)
		}
	}
	if len(config.Args) != 0 {
		t.Fatal("preparation mutated the original configuration")
	}
}

func TestYAMLOrderExpansionAndFlavor(t *testing.T) {
	config, err := parseYAML([]byte("path: child.exe\npath_flavors: {new: other.exe}\nargs: ['two', 'ten', '%VALUE%', '']\nenv: {VALUE: '%INPUT%'}\ncwd: '%dwd%'"))
	if err != nil {
		t.Fatal(err)
	}
	exe := filepath.Join(t.TempDir(), "tool.exe")
	plan, err := prepare(config, exe, []string{"cli"}, false, map[string]string{"INPUT": "ready", "DISPATCHER_tool_FLAVOR": "new"}, time.Now())
	if err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(plan.Args, []string{"two", "ten", "ready", "", "cli"}) {
		t.Fatal("YAML list/expansion changed")
	}
	if plan.Executable != "other.exe" || plan.CWD != filepath.Dir(exe) || !plan.UseJob {
		t.Fatal("flavor/CWD/defaults changed")
	}
}

func TestXMLExpansionUsesInheritedEnvironment(t *testing.T) {
	config := &Config{XML: map[string]string{"PATH": "child.exe", "ENV_VALUE": "override", "ARGV0": "%VALUE%", "ARGV1": "%Y%-%m%-%d%"}}
	plan, err := prepare(config, filepath.Join(t.TempDir(), "tool.exe"), nil, false, map[string]string{"VALUE": "inherited"}, time.Date(2026, 11, 13, 0, 0, 0, 0, time.UTC))
	if err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(plan.Args, []string{"inherited", "2026-11-13"}) || plan.Env["VALUE"] != "override" {
		t.Fatal("legacy expansion boundary changed")
	}
}

func TestUnportedModesAndConflictsFailBeforeExecution(t *testing.T) {
	for _, body := range []string{
		"app_name: Example!App\npath: child.exe",
		"path: child.exe\nas_service: true", "path: child.exe\nas_desktop_user: auto",
		"path: child.exe\nas_user: true", "path: child.exe\nuwf_servicing_detect: true",
		"path: child.exe\nvauth: {env: {map: {}}}\nenv_provider: {command: provider}",
	} {
		config, err := parseYAML([]byte(body))
		if err != nil {
			t.Fatal(err)
		}
		if _, err := prepare(config, filepath.Join(t.TempDir(), "tool.exe"), nil, false, map[string]string{}, time.Now()); err == nil {
			t.Fatal("invalid/unported mode accepted")
		}
	}
}

func TestInlineXMLVauthAndDuplicateRefusal(t *testing.T) {
	config := &Config{XML: map[string]string{}}
	body := `<?xml version="1.0"?><configuration><appSettings><add key="PATH" value="child.exe"/></appSettings><vauth><![CDATA[env: {map: {KEY: value}}]]></vauth></configuration>`
	if err := mergeXML(config, []byte(body), false); err != nil {
		t.Fatal(err)
	}
	if string(config.inlineRC) != "env: {map: {KEY: value}}" {
		t.Fatal("RC altered")
	}
	if err := mergeXML(config, []byte(body), false); err == nil {
		t.Fatal("duplicate vauth accepted")
	}
	if err := mergeXML(&Config{XML: map[string]string{}}, []byte(body), true); err == nil {
		t.Fatal("vauth fragment accepted")
	}
}

func ExampleConfig() {
	config, _ := parseYAML([]byte("path: child.exe\nargs: ['--profile', 'work']"))
	fmt.Println(config.Path, config.Args)
	// Output: child.exe [--profile work]
}
