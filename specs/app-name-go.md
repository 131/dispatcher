# Go — APP_NAME et contexte package

État au 14 novembre 2026 : implémenté, avec le C# conservé côte à côte.
Contrat observable commun : [portage-go.md](portage-go.md).

## Configuration et résolution

`app_name` en YAML ou `APP_NAME` en XML désigne
`PackageFamilyName!ApplicationId`, exclusif de PATH/flavors. Les arguments,
ENV, vauth inline/provider, CWD, OUTPUT, précommande, USE_JOB et DETACHED restent
ceux du dispatcher. Les combinaisons service, utilisateur alternatif/desktop
et restart réseau sont refusées, comme dans le contrat APP_NAME existant.

Les API app-model Windows énumèrent les packages enregistrés pour l'utilisateur
courant et leurs chemins d'installation. Le dispatcher lit `AppxManifest.xml`,
sélectionne la version la plus récente contenant l'application demandée et un
exécutable, puis exige un desktop/FullTrust (`Windows.FullTrustApplication`,
`packagedClassicApp` ou `win32App`). Les packages, applications et exécutables
manquants échouent explicitement ; pas de recherche de chemin par heuristique.

## Bootstrap et création de la cible

- Le même EXE Go est activé comme helper dans le contexte du package via
  l'interface COM interne `IDesktopAppXActivator` existante du C#. Son ABI et les
  options `4|16|32` restent isolés dans `package_windows.go`.
- L'entrée interne `--dispatcher-go-package-helper` ne charge pas les configs,
  le provider ou vauth : le parent envoie déjà l'environnement final.
- Un pipe local au nom aléatoire, limité au SID utilisateur et SYSTEM, transporte
  en mémoire args/CWD/flags/OUTPUT/ENV et les handles standard transférables.
  Le parent vérifie le PID client contre le process retourné par COM ; le helper
  vérifie le PID serveur contre son parent attendu.
- Le protocole Go interne utilise JSON encadré par une longueur 32 bits, maximum
  16 MiB, avec délais de lecture/écriture/connexion. Il ne doit pas être lu par
  C# et n'ajoute aucune version à la configuration YAML.
- Le helper attache la console parent si nécessaire, crée la cible suspendue
  avec un environnement explicite UTF-16 et les arguments encodés pour Windows,
  puis transmet son PID. Il ne reprend la cible qu'après confirmation du parent
  prêt à la surveiller. Une déconnexion/annulation avant handoff tue la cible.
- La gestion OUTPUT en ajout, le quoting et le bloc ENV réutilisent le chemin
  Win32 normal. Aucun token dans la CLI du helper, un log ou un fichier RC.
- USE_JOB est appliqué au helper avant création de la cible. L'assignation est
  best effort, avec diagnostic si le host package impose sa propre politique,
  comme le C#. Le job est fermé à la sortie du parent ; DETACHED n'attache pas
  de job et retourne après le handoff, sans attendre la cible.

Le COM reste un ABI Windows interne hérité, pas une garantie publique Windows.
Une application réutilisant une instance existante garde l'environnement de
cette instance ; le dispatcher ne prétend pas le remplacer.

## Validation

Tests hermétiques : validation XML/YAML, sélection de manifeste/version,
Unicode/valeurs vides/handles 64 bits, messages fragmentés, délais, helper sorti,
client imposteur, annulation avant reprise, ENV/CWD/ARGV/code retour/OUTPUT,
stdin/stdout/stderr pipés, job descendants et détachement.

Tests COM réels : un package desktop déjà installé sert **uniquement de
contexte** pour notre helper et notre probe. L'identité package est vérifiée
sur le probe exécuté. Les quatre helpers de production console/GUI × x86/x64
reçoivent ENV/ARGV/CWD et préservent le code retour. L'exécutable de l'application
utilisateur n'est pas lancé par ces probes et aucun package/config installé
n'est modifié.

Les tests COM locaux s'activent sous Windows avec
`DISPATCHER_GO_TEST_APP_NAME` (AUMID d'un package FullTrust installé) et
`DISPATCHER_GO_TEST_OUTPUT` (dossier des quatre binaires construits). Sans cette
configuration, ils sont explicitement skipped ; les tests de transport ne
prétendent pas prouver le COM à eux seuls. Pas de login Vault réel ni de release
ou migration des launchers dans cette validation.
