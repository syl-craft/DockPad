# Mises à jour Windows — NIN-80

DockPad utilise Velopack **1.2.161**, installé par utilisateur, x64, avec le runtime .NET 8 Desktop. L’installation et l’archive portable Velopack partagent le même flux GitHub Releases public, canal `win`. L’ancien ZIP sans métadonnées Velopack affiche un lien vers la dernière release ; il faut installer ou extraire une première version Velopack pour activer les mises à jour intégrées.

## Comportement

La recherche automatique démarre dans l’instance résidente après 20 secondes, puis au plus une fois toutes les six heures (date persistée). Elle se désactive dans **Mises à jour**. Un bouton apparaît dans la barre d’outils lorsqu’une version est disponible. La fenêtre affiche la version, les notes et la progression. Annuler interrompt le téléchargement ; un échec laisse une possibilité de réessayer. Rien ne s’installe automatiquement au démarrage.

Le clic **Mettre à jour et redémarrer** attend les écritures de favoris et demande de fermer les autres dialogues. Les secrets encore déposés par DockPad dans le presse-papiers sont retirés. Windows Restart Manager et une vérification des exécutables identifient les processus bloquants. La fenêtre indique leur nom, PID et chemin connu ; l’utilisateur choisit lesquels fermer, peut réessayer ou reporter. Une fermeture douce précède toute proposition d’arrêt forcé, avec confirmation distincte. Une erreur de détection interdit l’application de la mise à jour.

Les profils restent dans `%APPDATA%/DockPad` ou `DOCKPAD_PROFILE_DIR`. Un profil situé dans le dossier d’installation est refusé. Les chemins enregistrés pour le démarrage automatique, le navigateur et l’injection utilisent l’exécutable stable à la racine. Les commandes MCP proposées utilisent aussi ce chemin. Un ancien client MCP configuré avec un chemin absolu vers l’ancien ZIP ou vers `current/` doit être reconfiguré une fois ; DockPad ne réécrit pas les fichiers des clients IA.

Le lanceur racine transmet directement les arguments à `current/DockPad.exe`. Les événements d’installation et de redémarrage sont reconnus avant le relais. Le relais courant sort avant l’initialisation Velopack et la construction WPF ; l’entretien des paquets est réservé aux lancements persistants. Pendant l’application de la mise à jour, un marqueur retient les nouveaux lancements à la racine ; leurs arguments restent en mémoire. Les requêtes déjà acceptées mais pas encore traitées sont transférées au lanceur avant la sortie. Le pipe acquitte la prise en charge ; s’il ferme avant l’acquittement, le client utilise son repli. Il s’agit d’une remise au moins une fois, pas d’une transaction garantissant l’absence de doublon dans toute course de fermeture. Le marqueur expire après cinq minutes si l’updater ne revient pas.

## Adaptation native à maintenir

Le moteur de remplacement reste celui de Velopack. Toutefois, la version amont termine de force les processus du dossier d’installation. Cela contredit le consentement demandé pour les sessions MCP/IA. `tools/velopack/no-force-stop.patch` remplace ce comportement par un refus et propage l’erreur avant le remplacement. `stub.rs` évite le détour par `Update.exe` lors de chaque ouverture de lien, attend le marqueur et conserve les flux stdio MCP.

Ces trois binaires (`setup`, `update`, `stub`) sont construits depuis le commit amont `92d6a1c91716729d449034df5c50307dcce39493`. Leur licence MIT est incluse dans les paquets. **Ne pas remplacer seulement le SDK ou vpk par une nouvelle version** : réexaminer le patch et refaire les essais des paquets. Le packaging refuse de fonctionner sans les binaires adaptés. La CRT est liée statiquement. Prérequis de compilation : Rust 1.98.1, outils C++ MSVC x64 et Windows SDK, dans un Developer PowerShell.

```powershell
./tools/velopack/build-native.ps1
./tools/velopack/pack.ps1
./tools/velopack/test-packages.ps1
./tools/velopack/test-app.ps1
```

Le workflow manuel `package.yml` réalise ces opérations et conserve des artefacts **non signés**, sans publier de release. Le script de packaging lit la version du projet ; une branche de feature ne modifie pas cette version. Pour la production, passer `-SignTemplate` à `pack.ps1` avec la commande de signature mise en place dans NIN-9. Le modèle Velopack remplace `{{file}}` par le fichier à signer. Signer les binaires embarqués et le Setup final, puis calculer les hashes.

Publier dans une même release stable GitHub le Setup, le Portable ZIP, les paquets `full`/`delta` et `releases.win.json` produits ensemble. Le feed doit être publié seulement lorsque tous ses fichiers sont disponibles. Pour des deltas, conserver les paquets précédents dans le répertoire de sortie avant le packaging. Le client conserve la vérification des hashes et le repli vers le paquet complet de Velopack.

## WinGet — NIN-8, NIN-9 et NIN-6

L’identité durable est `packId=DockPad`, titre `DockPad`, éditeur `syl-craft`. L’entrée de désinstallation HKCU utilise `DockPad`, avec une `DisplayVersion` égale à la version du paquet. Une mise à jour intégrée met cette entrée à jour aussi, permettant à WinGet de reconnaître la version installée.

Le même Setup sert à WinGet avec `--silent`, scope utilisateur et `UpgradeBehavior: install`. Une session DockPad/MCP ouverte bloque une mise à niveau silencieuse : fermer les processus puis relancer `winget upgrade`. Le mode silencieux ne tue pas ces processus. Le ZIP portable n’est pas le paquet WinGet ; installer le Setup lors du passage à WinGet évite de supposer qu’une archive extraite est enregistrée dans Windows.

`pack.ps1` conserve aussi le nom immuable `DockPad-X.Y.Z-win-x64-Setup.exe`. Après la signature et la définition de la licence :

```powershell
./tools/velopack/winget.ps1 -Version X.Y.Z -Installer ./release/velopack/DockPad-X.Y.Z-win-x64-Setup.exe -License <identifiant> -LicenseUrl https://github.com/syl-craft/DockPad/blob/vX.Y.Z/LICENSE
winget validate --manifest ./release/winget/s/syl-craft/DockPad/X.Y.Z
```

Le générateur produit les trois manifestes (version, locale, installateur), exige une signature valide et calcule SHA256 sur le fichier final. La licence n’est pas inventée : elle dépend de NIN-6. La publication au dépôt communautaire et la configuration SignPath restent dans NIN-8/NIN-9.

## Vérifications

Les tests unitaires couvrent les états de recherche/téléchargement, annulation, reprise, double clic, échec d’application, chemins stables, profil, PID réutilisé, détection réelle par Restart Manager et transfert des liens en attente. `DialogShot updates` et `DialogShot update-blockers` rendent les fenêtres sans utiliser le profil réel. `test-packages.ps1` construit deux versions d’une application de test isolée, applique la mise à jour, vérifie le redémarrage, le transfert du lien, la conservation d’un fichier de profil et les flux MCP, puis vérifie qu’un MCP actif n’est pas tué par l’updater. Il vérifie aussi un verrou externe, la reprise après libération et l’installation/mise à niveau silencieuse avec la version ARP attendue, suivie de la désinstallation de la fixture.

Mesure locale du 29 septembre 2026, 20 passages après chauffe, application de test et pipe isolés : médiane **102,0 ms** pour runtime + relais direct, **109,7 ms** avec le lanceur stable et relais rapide, contre **151,6 ms** si l’initialisation Velopack est exécutée avant chaque relais. P95 : 123,7 / 153,2 / 183,4 ms respectivement. Cette mesure isole le coût du lanceur et du SDK ; ce n’est pas une mesure de l’ouverture complète du navigateur. Elle se reproduit avec `DockPad.UpdateProbe.exe --benchmark <racine du paquet de test>`.

`test-app.ps1` archive la révision git demandée (HEAD par défaut), remplace uniquement les identifiants des tubes/mutex/clés de registre par ceux de la fixture et compile **DockPad lui-même** en deux versions de test. Un bureau Windows séparé héberge les fenêtres, sans basculer le bureau interactif. `DOCKPAD_PROFILE_DIR` et un raccourci réservé à la fixture isolent le profil. Un hook CLR de test pilote la mise à jour via le SDK et l’arrêt normal de l’application. Avant/après, il vérifie l’enregistrement du raccourci et son handler WM_HOTKEY, les commandes de registre HTTP/HTTPS/autostart/injection, l’ouverture effective des fenêtres URL/injection et la lecture MCP. Les hashes des configurations sont comparés. L’événement Exit confirme la libération du mutex, du raccourci et des quatre tubes **avant la fin du processus**. Les clés de test sont supprimées ensuite. Aucune modification de UserChoice ou des clés DockPad habituelles.

Comparaison supplémentaire sur les vrais exécutables DockPad (mêmes sources, seuls les noms du mutex et des tubes sont isolés) : avant **94,6 ms** de médiane, après packaging **115,5 ms**, P95 127,3 / 174,6 ms, 20 passages après chauffe. `tools/RelayAcceptance` reçoit le chemin de l’ancien exécutable et celui du lanceur empaqueté. Un second passage avec `tools/NetworkProbe` dans `DOTNET_STARTUP_HOOKS` a attaché 50 sondes : aucun événement System.Net.Http, NameResolution ou Sockets dans les relais. Le lanceur natif ne contient aucun appel réseau. Ces mesures locales ne représentent pas le temps de démarrage du navigateur.

Avant la première diffusion signée, compléter sur une VM Windows propre : installation du Setup signé, détection puis mise à niveau via le dépôt WinGet réel, choix du navigateur par défaut par Windows et accès à un coffre/client MCP réel. Ces étapes nécessitent les artefacts de NIN-8/NIN-9 ; les tests isolés ne valident pas la confiance Authenticode ni la publication. Le retour à l’ancienne version en cas d’échec de remplacement dépend de Velopack ; aucun rollback automatique après un crash fonctionnel de la nouvelle application n’est promis.

Références : [cycle de vie Velopack](https://docs.velopack.io/integrating/overview), [source amont épinglée](https://github.com/velopack/velopack/tree/92d6a1c91716729d449034df5c50307dcce39493), [manifestes WinGet](https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema/1.10.0).
