package dispatcher

import (
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestVauthNativeDelegationAndOverridePriority(t *testing.T) {
	home := t.TempDir()
	t.Setenv("HOME", home)
	t.Setenv("USERPROFILE", home)
	creates := 0
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/v1/auth/token/lookup-self":
			fmt.Fprint(w, `{"data":{"ttl":2000,"entity_id":"fixture"}}`)
		case "/v1/identity/entity/id/fixture":
			fmt.Fprint(w, `{"data":{"metadata":{}}}`)
		case "/v1/auth/token/create/client-ro":
			creates++
			fmt.Fprint(w, `{"auth":{"client_token":"fixture-child","lease_duration":1000}}`)
		default:
			t.Errorf("unexpected SDK request %s", r.URL.Path)
			http.NotFound(w, r)
		}
	}))
	defer server.Close()
	if err := os.WriteFile(filepath.Join(home, ".vauthrc"), []byte("known_vaults: ['"+server.URL+"']"), 0600); err != nil {
		t.Fatal(err)
	}
	body := "path: child.exe\nargs: ['%ROUTER_TOKEN%', '%PRIORITY%']\nenv: {PRIORITY: static, CONTEXT: input}\nvauth:\n  vault_addr: " + server.URL + "\n  token_cache: false\n  delegate: [client-ro]\n  env:\n    map:\n      ROUTER_TOKEN: $${delegate.client-ro}\n      PRIORITY: dynamic\n      CONTEXT_SEEN: $${env.CONTEXT}\n"
	config, err := parseYAML([]byte(body))
	if err != nil {
		t.Fatal(err)
	}
	plan, err := prepare(config, filepath.Join(t.TempDir(), "tool.exe"), nil, false, map[string]string{"VAULT_TOKEN": "fixture-parent"}, time.Now())
	if err != nil {
		t.Fatal(err)
	}
	if creates != 1 || plan.Env["ROUTER_TOKEN"] != "fixture-child" || plan.Env["VAULT_TOKEN"] != "fixture-parent" || plan.Env["PRIORITY"] != "static" || plan.Env["CONTEXT_SEEN"] != "input" {
		t.Fatal("native SDK exports/precedence changed")
	}
	if plan.Args[0] != "fixture-child" || plan.Args[1] != "static" {
		t.Fatal("target argument expansion did not use final environment")
	}
}

func TestVauthUnknownVaultStopsPreparation(t *testing.T) {
	t.Setenv("HOME", t.TempDir())
	t.Setenv("USERPROFILE", t.TempDir())
	requests := 0
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { requests++; http.Error(w, "unexpected", 500) }))
	defer server.Close()
	config, err := parseYAML([]byte("path: child.exe\nvauth:\n  vault_addr: " + server.URL + "\n  known_vaults: ['" + server.URL + "']\n  env: {map: {KEY: value}}"))
	if err != nil {
		t.Fatal(err)
	}
	if _, err := prepare(config, filepath.Join(t.TempDir(), "tool.exe"), nil, false, map[string]string{"VAULT_TOKEN": "fixture-parent"}, time.Now()); err == nil || requests != 0 {
		t.Fatal("inline RC authorized its own Vault")
	}
}
