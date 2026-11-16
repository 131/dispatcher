# Dispatcher — contrat du portage Go

Date : 14 novembre 2026. Statut : contraintes/arbitrages validés ; socle Go et
`APP_NAME` implémentés, pas encore une release de parité. Le contrat SDK RC en
mémoire est confirmé et publié. État/build/tests : [GO.md](../GO.md).

## Demande et contraintes acquises

- Conserver les implémentations C# et Go côte à côte dans ce dépôt.
- Faire converger les contrats observables, pas les détails internes propres à
  chaque langage.
- Le livrable .NET doit rester compatible autant que possible avec l'existant.
  Une version SemVer majeure est autorisée, mais seuls des comportements
  marginaux/bizarres peuvent être exclus après arbitrage ; ce n'est pas une
  autorisation de supprimer des fonctionnalités courantes.
- Contrat .NET : **comme maintenant, EXE C# utilisant .NET Framework 4.8
  installé, sans runtime embarqué**, pour limiter la taille du binaire.
  **Cible de taille
  C# uniquement : < 100 Ko par exécutable distribué**, soit < 100 000 octets,
  signature incluse si le livrable est signé. fleurent indique que les releases
  actuelles font environ 80 Ko ; cette référence est déclarée, non mesurée ici.
  Runtime et taille sont acquis, pas des arbitrages ouverts.
- Contrat Go : exécutable autonome, configuration XML **et** YAML dans des
  fichiers portant uniquement l’extension **`.config`** : marqueur `<?` = XML,
  sinon YAML. Aucun `.yaml`/`.yml`, aucun fragment YAML, aucun champ de version
  YAML. Types et syntaxe documentés. Pas de contrainte de taille comparable à
  celle du .NET.
- Conserver le XML existant lié au contrat C#. Une syntaxe YAML spécifique au
  Go est autorisée ; sa lecture par C# n'est pas requise.
- **Binaires Go :** `dispatcher_go_cmd.exe` (x86/32 bits),
  `dispatcher_go_cmd_x64.exe`, `dispatcher_go_win.exe` (x86/32 bits),
  `dispatcher_go_win_x64.exe`. Les noms et binaires C# existants sont conservés.
- **Ligne Go 1.x : extension d'environnement vauth native** via `131/vvauth`,
  sections vauthrc directement dans la config dispatcher ; Go uniquement.
  Syntaxe et point d'intégration : [vauth-native.md](vauth-native.md).
- Présenter les arbitrages nécessaires **avant** de commencer l'implémentation.

## Référence initiale inspectée

Dépôt propriétaire vérifié par `origin` : `git@github.com:131/dispatcher.git`.
Checkout inspecté à `1a96f2f` ; `package.json` déclare `3.1.2`.
Sources de référence : [Program.cs](../src/Program.cs),
[EnvironmentProvider.cs](../src/Utils/EnvironmentProvider.cs),
[PackagedApplication.cs](../src/Utils/PackagedApplication.cs),
[build](../build), [tests locaux](../tests/packaged/run.ps1) et
[configuration/build existants](../README.md).
La suite historique est consommée via `dispatcher-tests` dans
[package.json](../package.json) ; son checkout a été consulté sans modification.
Ce relevé initial est une inspection. Les tests du premier incrément et les
releases SDK sont rapportés dans [GO.md](../GO.md) et
[vauth-native.md](vauth-native.md), sans valider les modes encore à porter.

## Socle de compatibilité à couvrir

| Domaine | Contrat à conserver ou vérifier entre C# et Go |
| --- | --- |
| Distribution | Exécutables renommables ; variantes console/GUI et Windows x86/x64 existantes ; ne pas écraser les artefacts C# par les artefacts Go. |
| Cible classique | `PATH`, résolution relative à l'exécutable dispatcher et recherche Windows ; flavors `PATH_<flavor>` via `DISPATCHER_<nom>_FLAVOR`. |
| Cible packagée | `APP_NAME=PackageFamilyName!ApplicationId`, exclusif de `PATH` ; environnement explicite, arguments, CWD, OUTPUT, attente/code retour ou détachement. |
| Arguments | `ARGV*` injectés avant les arguments reçus ; préserver les valeurs vides, Unicode, espaces, guillemets et antislashs. Le tri XML historique est traité dans les arbitrages. |
| Environnement | Héritage, `ENV_*`, noms Windows insensibles à la casse, expansion `%VAR%`, macros `%dwd%`, `%Y%`, `%m%`, `%d%`, `%H%`, `%i%`, `%s%`. Ne pas transformer silencieusement l'ordre d'expansion. |
| Vauth natif Go | Section `vauth` inline XML/YAML ; SDK `131/vvauth`, aucun processus vauth externe ; overrides explicites prioritaires, échec avant lancement si résolution impossible. Voir [contrat dédié](vauth-native.md). |
| Provider | `ENV_PROVIDER` : commande, stdout JSON objet de chaînes, erreur sur échec/timeout/JSON invalide ; timeout par défaut 5 s, `ENV_PROVIDER_TIMEOUT` en secondes, CWD hérité sauf `ENV_PROVIDER_CWD`. `ENV_*` visibles au provider et prioritaires sur son résultat. Aucune valeur sensible journalisée. |
| Processus | CWD, stdin/stdout/stderr, OUTPUT en ajout, code retour cible, variante sans fenêtre, Ctrl+C, `USE_JOB` et nettoyage des descendants, `DETACHED`. |
| Précommande | `PRESTART_CMD` avant la cible ; comportement à préciser sans lui appliquer arbitrairement le contrat du provider. |
| Services et identités | `AS_SERVICE`, `AS_DESKTOP_USER`, `AS_USER`, `USERNAME`, `PASSWORD`, variables de credentials, mode `auto`, restart réseau et UWF : fonctionnalités existantes, pas exclusions acquises. |

`APP_NAME` rejette actuellement services, autre utilisateur, utilisateur desktop
et restart réseau. La compatibilité Go ne doit pas promettre ces combinaisons.
Le helper Go est implémenté et testé en contexte package :
[app-name-go.md](app-name-go.md). Il utilise la même interface COM interne que
le C# ; ce risque existant doit rester visible. Une activation simplifiée ne peut pas
remplacer l'existant si elle perd ENV, ARGV ou le suivi du processus cible.

## Configuration XML

Conserver l'enveloppe `configuration/appSettings/add` avec `key` et `value`.
La découverte actuelle charge `<nom>.config`, puis `<nom>.exe.config`, puis
`<nom>.config.d/*.config`, à côté du dispatcher. Une valeur ultérieure écrase
la précédente. Les autres sections .NET ne constituent pas des options dispatcher.

Le code historique ne trie pas explicitement les fragments et absorbe les
erreurs de lecture/XML. **Correction implémentée pour C# et Go :** trier les
fragments ordinalement par nom et échouer sur un fichier présent invalide ou
illisible. Garder les fichiers facultatifs absents et la précédence ci-dessus.
Les options `ARGV*` conservent le tri historique `keys.Sort()`, sans conversion
en tri numérique. Les booléens XML acceptent `true/false` insensibles à la casse,
`1/0` et vide=faux ; les autres valeurs sont rejetées. Le restart réseau lit
réellement sa valeur booléenne. Détail :
[arbitrages acquis](arbitrages.md#2-nettoyage-des-comportements-historiques--acquis).

## Configuration YAML Go

La seule extension de configuration reste `.config`. Après retrait d'un
éventuel BOM et des espaces initiaux, le marqueur `<?` sélectionne XML ; sinon,
le contenu est lu en YAML. Une erreur XML n'entraîne pas de fallback YAML.
La configuration principale YAML est complète et ne charge pas de fragments.
Les fragments de configuration restent exclusivement XML.

Syntaxe détaillée dans [yaml-go.md](yaml-go.md), **sans champ de version**.
Elle ne s'applique pas au C# et n'impose pas de remodeler le XML. XML et YAML
doivent produire le même contrat d'exécution sur leur périmètre commun, sans
construire un framework partagé ni faire dépendre Go du CLR.

## Validation requise avant de déclarer la convergence

- Exécuter les suites historiques et locales contre les artefacts C# et Go,
  en adaptant uniquement la sélection du binaire, pas les attentes pour masquer
  une régression. Ajouter des fixtures comparatives lorsque nécessaire.
- Couvrir XML : précédence, fragments, flavors, expansion ENV/provider et ordre
  ARGV ; tester séparément toute rupture explicitement approuvée.
- Couvrir la détection de contenu `.config` (XML `<?`, sinon YAML), le rejet
  des fichiers invalides, l’absence de découverte `.yaml`/`.yml` et l’absence
  de chargement de fragments en mode YAML. Couvrir types, champs inconnus,
  ordre des listes et équivalence observable avec des fixtures XML.
- Tester sous Windows x86/x64 et console/GUI : quoting, flux, codes retour,
  erreurs de création, OUTPUT, jobs, détachement et contrôle console.
- Tester services/identités/UWF dans un environnement adapté ; absence de
  machine ou de droits = validation en attente, pas suppression de fonctionnalité.
- Tester `APP_NAME` avec un vrai package installé ; les tests de transport avec
  bootstrap de substitution ne prouvent pas à eux seuls la compatibilité COM.
- Mesurer les tailles C# avant/après, vérifier la cible < 100 Ko par EXE
  distribué et rapporter les tailles non signées et signées. Aucun budget de
  taille équivalent pour Go. Pas de taille affirmée sans mesure.

## Séquence de travail proposée

1. Partir des décisions acquises de distribution, XML/YAML et ruptures
   autorisées ; ne rouvrir un arbitrage que sur un blocage concret nouveau.
2. Contrat login confirmé et propagé dans les versions publiées selon
   [vauth-native.md](vauth-native.md).
   Caractériser les comportements communs par des tests ; établir la taille C#.
3. Ajouter Go avec chemins/artefacts distincts, sans déplacer les sources C#
   inutilement ; commencer par config et lancement classique.
4. Porter provider et vauth natif, cycle de vie, packages et services/identités ; des étapes
   intermédiaires ne constituent pas une release de parité.
5. Rejouer la matrice, documenter les écarts acceptés et préparer les artefacts.
   Signature, release et déploiement ne sont pas autorisés par ce cadrage.
