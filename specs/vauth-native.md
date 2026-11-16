# Go — extension native de l'environnement par vauth

Date : 14 novembre 2026. Statut : fonctionnalité demandée par fleurent pour
la ligne Go 1.x ; syntaxe et résolution native implémentées dans le premier
incrément, SDKs publiés. `APP_NAME` est maintenant implémenté dans le Go et
conserve les exports natifs, voir [app-name-go.md](app-name-go.md).

## Contrat

- **Go uniquement : intégrer la bibliothèque `131/vvauth`**, dont le module
  Go inspecté est `github.com/131/vvauth/v2`. Pas d'exécution de `vauth.exe`,
  Node, WSL ou PowerShell pour cette intégration native.
- **Accepter directement les sections d'un vauthrc dans le `.config`
  dispatcher**, sans imposer un fichier `.vauthrc` annexe ou `ENV_VAUTHRC`.
- C# garde le contrat existant et `ENV_PROVIDER` ; aucune dépendance vauth
  ajoutée au C# ni incidence sur son budget < 100 Ko.
- Le contenu vauth est une configuration explicite et approuvée par
  l'opérateur : cette fonctionnalité n'en fait pas un document non fiable
  pouvant être exécuté sans revue.

## Syntaxe YAML Go

Une clé facultative `vauth` contient le document vauthrc, **avec ses noms de
sections et ses macros inchangés**. L'enveloppe évite la collision entre `env`
(mapping de chaînes du dispatcher) et `vauth.env` (configuration du SDK).
Pas de champ de version ajouté au document.

```yaml
app_name: 'Example.Desktop_1234567890123!App'
args: ['--profile', 'work']
env:
  CLIENT_HOME: '%dwd%\profiles\work'
vauth:
  vault_addr: 'https://vault.example.test'
  ssh_auth:
    role: '$${env.VAUTH_USER_LOGIN}'
  delegate:
    - 'client-profile-ro'
  env:
    map:
      CLIENT_ROUTER_TOKEN: '$${delegate.client-profile-ro}'
```

`vault_addr`, `ssh_auth`, `jwt_auth`, `token_cache`, `known_vaults`, `delegate`,
`ssh-agent-crypt` et `env` gardent le contrat du SDK. Leur validation,
authentification, cache, délégation, profil/KV, macros et conversion en chaînes
appartiennent aux bibliothèques vauth, pas à un second moteur dispatcher.
Le type strict `env: mapping<string,string>` du dispatcher ne s'applique pas
aux valeurs `vauth.env.map`, qui suivent le contrat vauthrc.
Les macros `$${...}` ne sont pas réécrites par l'expansion `%VAR%` du dispatcher.

## Syntaxe XML Go

Pour garder la même fonctionnalité avec le format XML, ajouter un unique
élément `<vauth>` directement sous `<configuration>`. Il contient un document
vauthrc YAML comme texte, typiquement CDATA ; pas de nouvelle extension de
fichier ni de mini-schéma XML réinventant les sections du RC.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <appSettings>
    <add key="PATH" value="client.exe"/>
    <add key="ENV_CLIENT_HOME" value="%dwd%\profiles\work"/>
  </appSettings>
  <vauth><![CDATA[
vault_addr: https://vault.example.test
ssh_auth:
  role: $${env.VAUTH_USER_LOGIN}
delegate:
  - client-profile-ro
env:
  map:
    CLIENT_ROUTER_TOKEN: $${delegate.client-profile-ro}
  ]]></vauth>
</configuration>
```

La section vauth inline appartient au fichier principal, pas aux fragments
`.config.d`. Les fragments dispatcher XML restent possibles ; ils ne peuvent
pas définir ou fusionner des morceaux de vauthrc. Plusieurs sections vauth
inline : erreur. C# ne résout pas cette extension Go ; une config l'utilisant
ne doit pas être présentée comme équivalente sur C#.

## Ordre d'exécution et précédence

1. Charger et valider toute la configuration avant les effets externes.
2. Construire l'environnement hérité et les overrides explicites dispatcher.
   Ces overrides sont visibles par vauth, notamment pour `$${env.X}`.
3. Si `vauth` est présent, résoudre l'environnement via le SDK, en mémoire.
4. Fusionner les exports vauth puis réappliquer les overrides explicites du
   dispatcher : **les valeurs statiques explicites restent prioritaires**.
5. Développer les autres champs dispatcher selon leur contrat, puis exécuter
   précommande et cible. `PATH` et `APP_NAME` bénéficient de ces exports.

L'environnement retourné inclut les variables que le SDK exporte réellement,
pas seulement `env.map`. Ne pas réduire silencieusement le résultat ou
remplacer un token délégué par un token parent en cas d'échec.
Une section `vauth` présente et `ENV_PROVIDER` / `env_provider` présent :
erreur de configuration, afin de ne pas authentifier deux fois ni inventer
une précédence entre deux sources dynamiques. Sans section `vauth`, le provider
existant reste disponible ; la présence de `ENV_VAUTHRC` seule n'active pas
l'intégration native.

Erreur de configuration, login, agent, délégation, macro ou lecture de secret :
échec avant de lancer précommande/cible. Aucun lancement avec exports partiels.
Ne jamais écrire les tokens, secrets, RC rendu ou exports sur stdout, dans
OUTPUT, logs ou fichiers temporaires. Garder les diagnostics sans valeurs
sensibles. Le cache chiffré SDK reste propriétaire de ses fichiers persistants.

## Référence réelle inspectée, sans valeurs sensibles

Le launcher `D:\apps\bundle\bin\chatgpt-el.config` utilise actuellement XML,
`APP_NAME`, `ENV_PROVIDER`, son CWD/timeout et `ENV_VAUTHRC`, avec des overrides
ENV et arguments. Son RC annexe contient `vault_addr`, `ssh_auth.role`,
`delegate` et `env.map`. L'intégration demandée permet de déplacer ces sections
dans `vauth` et de retirer le provider/chemin RC sur la variante Go.
Aucun fichier installé n'a été modifié ; aucun login réel n'a été exécuté.
Les exemples ci-dessus sont synthétiques, pas une copie de cette configuration.

## État SDK publié et consommé

Contrat RC en mémoire confirmé par fleurent ; releases produites avec
`ppackage version minor`, commits et tags poussés :

- `vauth-login v1.3.0`, commit `ba56fb3` :
  `NewFromRC(body []byte, environ map[string]string) (*Vauth, error)`.
- `vvauth v2.8.0`, commit `227e6b4` : même constructeur, compilation des sections
  ENV/profil depuis le snapshot du client login puis exports via `Env(false)`.

Le contrat définissant chaque API est documenté dans son `API_and_tests.md`.
Le home natif `~/.vauthrc` reste source de confiance pour les Vaults sélectionnés,
sans fusion implicite de configuration. `RC()` conserve le document complet.

CI des deux releases réussies. Artefacts GitHub Linux/Windows téléchargés et
vérifiés avec `SHA256SUMS` ; table Authenticode présente dans les Windows.
Packages npm aux versions exactes téléchargés et intégrités vérifiées.
Le dispatcher consomme `vvauth v2.8.0` et indirectement `vauth-login v1.3.0`
dans son `go.mod`/`go.sum`, sans workspace de substitution ou `replace`.

Tests SDK : Go avec race detector et vet, builds Windows 386/amd64 ; Node
90/90 pour login et 97/97 pour vvauth. Tests dispatcher : délégation inline
loopback, visibilité/priorité des overrides et refus du Vault inconnu ; smoke
XML/YAML des quatre artefacts Go renommés sur Windows. Le bootstrap COM réel
et les quatre helpers APP_NAME sont désormais validés sur un probe en contexte
package. Les services et une connexion Vault réelle ne sont pas validés.
Aucun launcher installé, service ou credential personnel n'a été modifié.

## Matrice de validation native

- XML et YAML inline : mêmes exports et mêmes erreurs sur fixtures locales.
- Agent/login/cache/délégation testés via fixtures et Vault de test ; pas de
  token personnel capturé dans les tests.
- Overrides visibles au SDK et prioritaires sur ses exports ; macros vauth
  non altérées ; noms d'environnement Windows gérés sans collision de casse.
- Refus provider + vauth, RC invalide, échec SDK, aucune cible lancée sur erreur.
- Mode sans vauth inchangé ; C# sans dépendance native supplémentaire.
- Vérifier les variantes Go x86/x64 console/GUI, y compris agent Windows.
