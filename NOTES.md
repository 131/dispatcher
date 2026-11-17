# Dispatcher v4.0.2 — C# + Go

Huit EXE Windows signés : console/GUI × x86/x64, C# et Go côte à côte.
C# reste petit, portable sous Windows et dépendant de .NET Framework 4.8 installé ;
Go est autonome. La taille C# (~100 Ko visés) est une guideline, pas un test.

- Go : XML ou YAML dans `.config` uniquement, vauthrc inline via `vvauth`, et
  `APP_NAME` natif avec ENV/ARGV/CWD, OUTPUT, attente ou détachement.
- Ruptures communes : config présente invalide/illisible = erreur, fragments XML
  triés, booléens explicites, `SERVICE_RESTART_ON_NETWORK_CHANGE=false` respecté.
  Ordre `ARGV*` XML conservé ; YAML utilise une liste.

## Pas encore supporté

- **Go** : services Windows, utilisateurs alternatifs/desktop, UWF et restart
  réseau. Refus explicite ; ces fonctions restent dans le C#.
- **C#** : YAML et vauth inline natif ; `ENV_PROVIDER` reste disponible.
- **APP_NAME, les deux** : desktop/FullTrust, utilisateur interactif uniquement ;
  pas d'UWP/services/autre utilisateur. ABI COM Windows interne ; une instance
  d'application déjà ouverte conserve son environnement.
- Pas de fragments YAML, de champ de version YAML ni d'extension `.yaml/.yml`.

Tests : config/ENV/providers, quoting/Unicode, stdio, OUTPUT, codes retour,
jobs/détachement, annulation et transport package ; COM réel sur probe quand
un package desktop est disponible. Aucun token personnel utilisé.
