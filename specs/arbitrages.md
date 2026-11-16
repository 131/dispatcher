# Arbitrages avant implémentation

Date : 13 novembre 2026. Les décisions explicites de fleurent sont identifiées comme acquises ;
**les recommandations restent des propositions à confirmer**.
Contraintes acquises : [contrat du portage](portage-go.md).

## 1. Livrable .NET et taille — acquis

**Runtime acquis : comme maintenant**, un EXE C# utilisant .NET Framework
4.8 installé, sans runtime embarqué, pour limiter la taille du binaire.
Ce point ne nécessite plus d'arbitrage. Référence existante :
[build](../build), [projet](../src/Dispatcher.csproj).

**Guideline confirmée par fleurent :** garder le C# petit, portable sous Windows
et dépendant du runtime installé, avec < 100 Ko comme référence indicative
(releases actuelles ~80 Ko selon fleurent). **Pas de seuil strict ni de test de
taille** en CI/release. Les tailles peuvent être rapportées à titre informatif.
Pas de contrainte de taille équivalente pour Go.

## 2. Nettoyage des comportements historiques — acquis

Observations dans [Program.cs](../src/Program.cs) et décisions validées par
fleurent pour C# et Go, en SemVer major si nécessaire :

| Comportement observé | Décision / traitement | Impact |
| --- | --- | --- |
| `catch { }` sur lecture/XML ; fragments sans tri explicite | Fichier absent facultatif ; fichier présent illisible/invalide = erreur. Trier les fragments ordinalement par nom, dernière valeur gagnante. | C# et Go peuvent désormais refuser une config anciennement ignorée ; résultat différent pour des fragments concurrents. |
| `toBool` : seuls vide, `false`, `0` sont faux ; toute autre chaîne est vraie | XML : accepter `true/false` insensibles à la casse et `1/0`, garder vide=faux ; rejeter les autres chaînes. YAML : booléens typés. | `False` cesse d'être vrai ; valeurs arbitraires comme `yes` deviennent invalides. |
| La simple présence de `SERVICE_RESTART_ON_NETWORK_CHANGE` active l'option | Lire sa valeur booléenne réellement. | Une ancienne valeur `false` cessera d'activer le restart. |
| Tous les `ARGV*` suivent `keys.Sort()` : `ARGV10` avant `ARGV2` | **Conserver l'ordre XML historique**, le caractériser ; YAML utilise une liste ordonnée. Ne pas effectuer un tri numérique implicite. | Pas de rupture ARGV imposée pour embellir le design ; la culture de tri .NET doit être caractérisée sur les noms non ASCII avant de promettre une émulation exacte. |
| Environnement construit en plusieurs passes ; expansion via l'environnement du processus | Caractériser et conserver les cas usuels XML ; pas de résolveur récursif nouveau. YAML : expansion explicite à étapes fixes. | Les références cycliques/cas dépendant d'un ordre non défini restent à inventorier avant exclusion. |

**Acquis :** fichier présent invalide/illisible = erreur ; fragments triés par
nom, dernière valeur gagnante ; booléens explicites ; restart réseau lisant sa
valeur. Ordre historique `ARGV*` conservé en XML, ordre de liste en YAML.
Aucune autre rupture de compatibilité n'est autorisée implicitement ; les cas
ENV atypiques restent à caractériser, pas à supprimer par défaut.

## 3. Sélection XML / YAML en Go — acquis

**Décision de fleurent : seule l'extension `.config` est supportée.**
Le contenu détermine le format : marqueur initial `<?` = XML comme maintenant ;
sinon, lecture YAML. Aucun fichier `.yaml` ou `.yml` n'est découvert.
Le marqueur est recherché après un éventuel BOM et les espaces initiaux.

- Go conserve les noms de fichiers historiques `<nom>.config` et
  `<nom>.exe.config`, tous deux avec l'extension `.config`.
- Le format est choisi par le contenu, pas par deux familles d'extensions :
  aucun arbitrage de priorité XML/YAML ni de « deux formats présents ».
- Une erreur de parsing XML sélectionné ne déclenche pas un second essai YAML.
  Un contenu YAML invalide échoue également : pas de configuration ignorée.
- Les fragments `<nom>.config.d/*.config` restent XML uniquement ; **aucun
  fragment YAML**. Une configuration principale YAML est complète, sans
  chargement de fragments.
- **YAML typé avec une spécification, sans champ de version.** C# reste XML.
- Pas de flag CLI consommant les arguments destinés à la cible.

Syntaxe détaillée : [yaml-go.md](yaml-go.md).

## 4. Services, identités et application packagée

**Recommandation sans réduction de périmètre :** conserver le contrat des
services/identités/UWF et `APP_NAME`. Porter par étapes et demander un arbitrage
supplémentaire seulement si un blocage concret exige une exclusion.

Les tests historiques comprennent service NT et utilisateur interactif ; ce
n'est donc pas un cas marginal démontré. Le remplacement des threads/handles
C# par des mécanismes Go n'a pas besoin d'un arbitrage tant que les effets
observables sont conservés. Les races, fuites et erreurs natives ignorées ne
sont pas des fonctionnalités à reproduire volontairement.

L'interface COM interne de `APP_NAME` est un risque hérité identifié dans
[PackagedApplication.cs](../src/Utils/PackagedApplication.cs). Proposer un autre
bootstrap seulement après avoir montré qu'il préserve ENV/ARGV/CWD, isolation,
attente et code retour ; pas de fallback shell perdant ces propriétés.

Pas de décision bloquante demandée ici : aucune suppression proposée. Le
cadrage vise Windows ; Linux/macOS et ARM64 seraient une extension distincte,
pas une conséquence implicite du choix de Go.

## 5. Vauth natif Go — contrat confirmé, SDKs publiés

La ligne Go 1.x doit intégrer `131/vvauth` et accepter les sections vauthrc
directement dans `.config`. Noms de binaires Go fixés dans le contrat.

Syntaxe implémentée : section `vauth` contenant le RC ; le SDK conserve son
contrat. fleurent a confirmé le contrat et autorisé releases/propagation.
`vauth-login v1.3.0` et `vvauth v2.8.0` sont publiés et consommés par le premier
incrément Go, avec vérification du home natif pour les Vaults autorisés.
Pas de fichier RC temporaire plaintext, de shell intermédiaire ou de
réimplémentation de l'authentification dans le dispatcher.
Détails et état vérifié : [vauth-native.md](vauth-native.md).
