package dispatcher

import (
	"bytes"
	"encoding/binary"
	"encoding/json"
	"encoding/xml"
	"fmt"
	"io"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

const packageTimeout = 15 * time.Second
const maximumPackageMessage = 16 * 1024 * 1024

func validateAppName(name string) error {
	family, app, found := strings.Cut(name, "!")
	if !found || family == "" || app == "" || strings.Contains(app, "!") || strings.ContainsRune(name, 0) {
		return fmt.Errorf("APP_NAME must be a PackageFamilyName!ApplicationId")
	}
	return nil
}

type manifestApplication struct {
	Attributes []xml.Attr `xml:",any,attr"`
}

func (app manifestApplication) attribute(name string) string {
	for _, attr := range app.Attributes {
		if strings.EqualFold(attr.Name.Local, name) {
			return attr.Value
		}
	}
	return ""
}

type packageManifest struct {
	XMLName  xml.Name `xml:"Package"`
	Identity struct {
		Version string `xml:"Version,attr"`
	} `xml:"Identity"`
	Applications []manifestApplication `xml:"Applications>Application"`
}

// selectPackageExecutable uses registered package directories, never a registry path guess.
func selectPackageExecutable(appName string, directories []string) (string, error) {
	if err := validateAppName(appName); err != nil {
		return "", err
	}
	_, appID, _ := strings.Cut(appName, "!")
	var selected [4]uint64
	path, desktop := "", false
	for _, directory := range directories {
		body, err := os.ReadFile(filepath.Join(directory, "AppxManifest.xml"))
		if err != nil {
			return "", fmt.Errorf("cannot read APP_NAME package manifest")
		}
		var manifest packageManifest
		if err := xml.Unmarshal(body, &manifest); err != nil {
			return "", fmt.Errorf("invalid APP_NAME package manifest")
		}
		version, err := packageVersion(manifest.Identity.Version)
		if err != nil {
			return "", err
		}
		for _, app := range manifest.Applications {
			if app.attribute("Id") != appID || app.attribute("Executable") == "" || (path != "" && !newerPackage(version, selected)) {
				continue
			}
			path = app.attribute("Executable")
			if !filepath.IsAbs(path) {
				path = filepath.Join(directory, path)
			}
			path, err = filepath.Abs(path)
			if err != nil {
				return "", fmt.Errorf("invalid APP_NAME executable path")
			}
			behavior := app.attribute("RuntimeBehavior")
			desktop = app.attribute("EntryPoint") == "Windows.FullTrustApplication" || behavior == "packagedClassicApp" || behavior == "win32App"
			selected = version
		}
	}
	if path == "" {
		return "", fmt.Errorf("APP_NAME application is missing from the package manifest")
	}
	if !desktop {
		return "", fmt.Errorf("APP_NAME requires a desktop / FullTrust application")
	}
	if info, err := os.Stat(path); err != nil || info.IsDir() {
		return "", fmt.Errorf("APP_NAME executable is missing")
	}
	return path, nil
}

func packageVersion(value string) ([4]uint64, error) {
	var version [4]uint64
	parts := strings.Split(value, ".")
	if len(parts) != 4 {
		return version, fmt.Errorf("invalid APP_NAME package version")
	}
	for i, part := range parts {
		n, err := strconv.ParseUint(part, 10, 16)
		if err != nil {
			return version, fmt.Errorf("invalid APP_NAME package version")
		}
		version[i] = n
	}
	return version, nil
}

func newerPackage(a, b [4]uint64) bool {
	for i := range a {
		if a[i] != b[i] {
			return a[i] > b[i]
		}
	}
	return false
}

type packageHello struct {
	Version    int    `json:"version"`
	PID        uint32 `json:"pid"`
	Executable string `json:"executable"`
}

type packageRequest struct {
	Args       []string          `json:"args"`
	CWD        string            `json:"cwd"`
	ShowWindow bool              `json:"show_window"`
	Detached   bool              `json:"detached"`
	Output     string            `json:"output"`
	ParentPID  uint32            `json:"parent_pid"`
	Handles    [3]uint64         `json:"handles"`
	Env        map[string]string `json:"env"`
}

type packageResult struct {
	PID   uint32 `json:"pid,omitempty"`
	Ready bool   `json:"ready"`
	Error string `json:"error,omitempty"`
}

// Frames are private to Go; public compatibility does not depend on BinaryWriter internals.
func writePackageMessage(conn net.Conn, value any, timeout time.Duration) error {
	body, err := json.Marshal(value)
	if err != nil || len(body) > maximumPackageMessage {
		return fmt.Errorf("invalid package helper message")
	}
	if err := conn.SetWriteDeadline(time.Now().Add(timeout)); err != nil {
		return err
	}
	var header [4]byte
	binary.LittleEndian.PutUint32(header[:], uint32(len(body)))
	if _, err := io.Copy(conn, bytes.NewReader(header[:])); err != nil {
		return fmt.Errorf("cannot write package helper message: %w", err)
	}
	if _, err := io.Copy(conn, bytes.NewReader(body)); err != nil {
		return fmt.Errorf("cannot write package helper message: %w", err)
	}
	return nil
}

func readPackageMessage(conn net.Conn, value any, timeout time.Duration) error {
	if err := conn.SetReadDeadline(time.Now().Add(timeout)); err != nil {
		return err
	}
	var header [4]byte
	if _, err := io.ReadFull(conn, header[:]); err != nil {
		return fmt.Errorf("cannot read package helper message: %w", err)
	}
	size := binary.LittleEndian.Uint32(header[:])
	if size == 0 || size > maximumPackageMessage {
		return fmt.Errorf("invalid package helper message length")
	}
	body := make([]byte, int(size))
	if _, err := io.ReadFull(conn, body); err != nil {
		return fmt.Errorf("cannot read package helper message: %w", err)
	}
	decoder := json.NewDecoder(bytes.NewReader(body))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(value); err != nil {
		return fmt.Errorf("invalid package helper message")
	}
	if _, err := decoder.Token(); err != io.EOF {
		return fmt.Errorf("invalid trailing package helper data")
	}
	return nil
}
