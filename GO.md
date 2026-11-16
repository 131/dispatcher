# Dispatcher Go — état du portage

Le C# reste dans `src/`, avec ses noms de binaires existants. Le Go est dans
`cmd/dispatcher-go` et `internal/dispatcher`. Les contraintes et décisions
restent définies dans [specs/portage-go.md](specs/portage-go.md).

## État implémenté

- Windows, console/GUI, x86/x64 ; exécutable autonome sans CLR.
- Configuration `.config` uniquement : `<?` sélectionne XML, sinon YAML.
- XML : noms/précédence historiques, fragments triés, `ARGV*` en ordre
  historique ; config présente invalide/illisible refusée. YAML : liste ARGV,
  types stricts, aucun champ de version et aucun fragment.
- `PATH`, flavors, arguments CLI, héritage ENV, overrides, macros, CWD.
- **`APP_NAME` / `app_name`** : package desktop/FullTrust, résolution par manifeste,
  helper COM en contexte package, environnement explicite, code retour/attente
  ou détachement. Détails : [specs/app-name-go.md](specs/app-name-go.md).
- Provider externe JSON avec timeout, ou **vauth inline natif** via
  `github.com/131/vvauth/v2 v2.8.0` et `vauth-login v1.3.0` ; sources publiées,
  aucun `replace` ni RC temporaire. Exemples :
  [YAML](specs/yaml-go.md), [vauth XML/YAML](specs/vauth-native.md).
- Lancement Win32 explicite, Unicode/quoting, handles standard, OUTPUT en ajout,
  code retour, job attaché avant reprise de la cible et nettoyage des descendants,
  `DETACHED` sans attente. Les flags de fenêtre suivent le contrat natif existant.
- Précommande exécutée avant la cible ; son code non nul n'empêche pas le
  lancement, comme le chemin C# classique, mais un échec de création échoue.

**Pas encore porté : services, identités alternatives, UWF et restart
réseau.** Les modes activés correspondants échouent explicitement avant une
résolution vauth/provider ou un lancement ; pas de fallback dégradé. Ils restent
au contrat final. Linux/macOS ne sont pas des cibles d'exécution.

Le C# implémente également les corrections validées : booléens explicites,
restart réseau lisant sa valeur, fragments triés et erreurs de lecture/XML
remontées. Sa version de livraison n'a pas été changée et aucune release
dispatcher n'a été publiée.

## Build

```bash
./build-go
```

| Artefact | Architecture |
| --- | --- |
| `output/go/dispatcher_go_cmd.exe` | x86 / 32 bits |
| `output/go/dispatcher_go_cmd_x64.exe` | x64 |
| `output/go/dispatcher_go_win.exe` | x86 / 32 bits |
| `output/go/dispatcher_go_win_x64.exe` | x64 |

`output/go/` sépare ces artefacts des quatre C# dans `output/`. Le script C#
`./build --build` et le workflow de release existant restent distincts. Les
binaires Go locaux ne sont pas signés et ne constituent pas une release finale.

## Tests

```bash
go test -race ./...
go vet ./...
```

Les tests de configuration et du SDK utilisent un Vault loopback et des homes
isolés, jamais des credentials personnels. Les tests Win32 vérifient quoting,
code retour, ENV/CWD, ajout OUTPUT, jobs, détachement et création en erreur.
Les tests package ajoutent peer identity, annulation avant reprise, pipes
stdin/stdout/stderr et handoff en contexte package. Le COM réel et les quatre
helpers de production sont testés localement sur un probe, sans démarrer
l’application utilisateur ; ils restent opt-in en CI via
`DISPATCHER_GO_TEST_APP_NAME`.

Sous Windows, après `./build-go`, les smoke tests des quatre artefacts renommés
s'activent via `DISPATCHER_GO_TEST_OUTPUT` :

```powershell
$env:DISPATCHER_GO_TEST_OUTPUT = (Resolve-Path .\output\go).ProviderPath
go test ./...
```

Depuis WSL, un test binaire Windows peut être cross-compilé puis exécuté avec
le même environnement fourni **dans le processus Windows** : une variable de
shell WSL seule n'est pas automatiquement transmise.

Les tests C# locaux restent `tests/packaged/run.ps1`. Ils incluent maintenant
les corrections de config validées. La suite historique `dispatcher-tests` et
les modes encore à porter restent à intégrer à la validation complète de parité.
