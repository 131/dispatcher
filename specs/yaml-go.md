# YAML Go — syntaxe et types

Statut : syntaxe implémentée dans le premier incrément Go ; modes d’exécution
encore à porter listés dans [GO.md](../GO.md). Décision de fleurent : YAML
exclusivement en Go, **dans un fichier `.config`, sans champ de version ni
fragments YAML**. Si le contenu commence par `<?` (après BOM/espaces), il est
lu comme XML ; sinon, comme YAML. Aucune extension `.yaml`/`.yml` supportée.
Ce document définit les champs et types ; exemples à placer dans `<nom>.config`.

## Exemple minimal

```yaml
path: '..\tools\node.exe'
args:
  - '-p'
  - 'process.env.DEMO'
env:
  DEMO: 'hello'
```

## Exemple avec vauth natif

```yaml
path: 'client.exe'
env:
  CLIENT_HOME: '%dwd%\profiles\work'
vauth:
  vault_addr: 'https://vault.example.test'
  ssh_auth:
    role: '$${env.VAUTH_USER_LOGIN}'
  delegate: ['client-profile-ro']
  env:
    map:
      CLIENT_ROUTER_TOKEN: '$${delegate.client-profile-ro}'
```

Sections vauthrc intégrées directement au `.config`, sans fichier annexe ni
champ de version. Les champs `env` et `vauth.env` ont des contrats différents.

## Exemple avec provider

```yaml
path: 'C:\tools\client.exe'
cwd: '%dwd%'
args: ['--profile', 'work', '']
env:
  CLIENT_PROFILE: 'work'
  ENABLE_FEATURE: '1'
env_provider:
  command: 'C:\tools\credentials.exe'
  args: ['env', '--json']
  cwd: '%dwd%'
  timeout_seconds: 60
output: '%dwd%\logs\client-%Y%-%m%-%d%.log'
use_job: true
detached: false
```

## Champs et types

| Champ | Type et correspondance |
| --- | --- |
| `path` / `app_name` | Chaîne ; exactement une cible, correspond à PATH / APP_NAME. |
| `path_flavors` | Mapping de noms de flavor vers des chemins ; sélection via `DISPATCHER_<nom>_FLAVOR` existant, interdit avec `app_name`. |
| `args` | Liste de chaînes, avant les arguments CLI reçus ; pas de découpage shell. |
| `env` | Mapping de chaînes vers chaînes ; `env: {FLAG: 'true'}` et non `env: {FLAG: true}`. |
| `cwd`, `output` | Chaînes facultatives ; héritage du CWD / pas de fichier OUTPUT si absent. |
| `vauth` | Document vauthrc inline ; noms/types/macros du SDK, voir [vauth-native.md](vauth-native.md). Exclusif de `env_provider`. |
| `env_provider` | Objet `command` (exécutable), `args` (liste), `cwd` (chaîne facultative), `timeout_seconds` (entier positif, défaut 5 ; plage commune XML). |
| `prestart` | Objet `command` et `args`, CWD de la cible ; attend la fin. Code non nul : même politique que le contrat XML caractérisé, pas une nouvelle politique implicite. |
| `use_showwindow`, `use_job`, `detached` | Booléens ; défauts du contrat commun console/GUI, job activé et détachement désactivé. |
| `as_service`, `as_desktop_user` | Booléen ou chaîne exacte `auto`, en correspondance avec XML. |
| `as_user` | Booléen. |
| `username`, `password` | Chaînes ; mêmes defaults credentials que XML ; ne pas journaliser les valeurs. |
| `uwf_servicing_disabled`, `uwf_servicing_detect`, `service_restart_on_network_change` | Booléens ; mêmes fonctionnalités que XML, avec les corrections approuvées. |

`command` désigne un fichier exécutable, pas une ligne shell ; si un shell est
souhaité, le déclarer comme exécutable avec ses arguments. Le XML garde
`ENV_PROVIDER` comme ligne de commande pour la compatibilité existante.

## Règles de lecture

- Un document YAML unique, mapping à la racine ; UTF-8.
- Champs inconnus, clés dupliquées, types invalides et options
  mutuellement exclusives : erreur avant de lancer une commande.
- Sous Windows, refuser deux clés `env` identiques à la casse près. Conserver
  la casse des noms de configuration ; aucune coercition YAML implicite pour
  les arguments ou les valeurs d'environnement.
- Ancres, aliases, merge keys et tags personnalisés hors syntaxe : erreur,
  plutôt qu'une dépendance à des comportements de parseur non spécifiés.
- Appliquer les macros de date à partir d'un instant unique. Expansion simple
  `%VAR%`, sans interpolation shell, templates ou résolution récursive.
- Étapes ENV : environnement hérité ; expansion des `env` depuis cet héritage
  (pas de références entre entrées) ; provider **ou** vauth natif avec ces
  overrides ; fusion des exports dynamiques ; réapplication des valeurs `env`
  déjà calculées ; expansion
  des autres champs depuis l'environnement final. Variable absente inchangée.
- Chemins : `path` relatif au dossier dispatcher si le fichier correspondant
  existe, sinon résolution Windows comme XML ; CWD/OUTPUT restent résolus
  selon les règles communes caractérisées, sans les rebasser tous sur `%dwd%`.
- Defaults et erreurs de combinaisons `APP_NAME` suivent le contrat commun.
  La syntaxe YAML n'autorise pas des combinaisons que le moteur ne supporte pas.

La séquence ENV YAML est intentionnellement plus explicite que les passes
historiques XML. La compatibilité exigée porte sur XML ; on ne réécrit pas son
expansion pour la faire ressembler au YAML.
