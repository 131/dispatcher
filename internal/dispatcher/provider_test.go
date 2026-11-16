package dispatcher

import (
	"fmt"
	"os"
	"strings"
	"testing"
	"time"
)

func TestProviderHelper(t *testing.T) {
	if os.Getenv("DISPATCHER_PROVIDER_HELPER") != "1" {
		return
	}
	if os.Getenv("PROVIDER_SLOW") == "1" {
		time.Sleep(2 * time.Second)
	}
	if body := os.Getenv("PROVIDER_BAD"); body != "" {
		fmt.Fprint(os.Stdout, body)
		os.Exit(0)
	}
	fmt.Fprintf(os.Stdout, `{"FROM_PROVIDER":"ready","PRIORITY":"dynamic","SEEN":"%s"}`, os.Getenv("PRIORITY"))
	os.Exit(0)
}

func TestProviderObjectAndOverrideInput(t *testing.T) {
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	environ := map[string]string{"DISPATCHER_PROVIDER_HELPER": "1", "PRIORITY": "static"}
	values, err := runProvider(Command{Command: exe, Args: []string{"-test.run=^TestProviderHelper$"}}, environ)
	if err != nil {
		t.Fatal(err)
	}
	if values["FROM_PROVIDER"] != "ready" || values["SEEN"] != "static" {
		t.Fatal("provider did not receive explicit environment")
	}
	for _, body := range []string{`{"secret":true}`, `{"secret":null}`, `{"secret":[]}`, `null`, `{} {}`} {
		environ["PROVIDER_BAD"] = body
		if _, err := runProvider(Command{Command: exe, Args: []string{"-test.run=^TestProviderHelper$"}}, environ); err == nil || strings.Contains(err.Error(), "secret") {
			t.Fatal("invalid provider output accepted or disclosed")
		}
	}
	delete(environ, "PROVIDER_BAD")
	environ["PROVIDER_SLOW"] = "1"
	if _, err := runProvider(Command{Command: exe, Args: []string{"-test.run=^TestProviderHelper$"}, TimeoutSeconds: 1}, environ); err == nil || !strings.Contains(err.Error(), "1000 ms") {
		t.Fatal("provider timeout not enforced")
	}
}
