# Changelog

## [1.11.0] — 2026-08-24

### Nouveautés utilisateur

#### Français et anglais
- DockPad parle **français et anglais**. Le choix se fait dans ☰ → Paramètres → **Options**, tout en haut : `Automatique (Windows)`, `Français` ou `English`
- **Bascule immédiate**, sans redémarrer : les fenêtres ouvertes se retraduisent sous les yeux, y compris la grille derrière et son bandeau
- Par défaut, DockPad suit la **langue de Windows** et retombe sur l'anglais si elle n'est pas traduite
- **Les nombres et les heures suivent aussi** : `12,4k` et `11h54` en français, `12.4k` et `11:54` en anglais. Les milliards s'écrivent `2,7 Md` ou `2.7B` selon la langue
- **Les pluriels sont justes**, y compris là où les deux langues ne basculent pas au même endroit : « 0 règle » mais « 0 rules »
- **Les libellés des prédéfinis sont traduits.** Les entrées déjà posées dans le menu clic droit de Windows gardent l'ancien libellé jusqu'à ce que tu cliques sur *Installer / Mettre à jour* dans la fenêtre Prédéfinis — elles y apparaissent maintenant comme « Mise à jour disponible »

#### Verrou du déplacement des tuiles
- Un bouton dans la toolbar, à gauche de **─** : **🔒** verrouillé, **✓** bleu quand le déplacement est ouvert. Un clic manqué de quelques pixels ne déplace plus la tuile qu'on voulait lancer
- Le verrou **ne ferme qu'une porte** : le glissement d'une tuile vers une autre. Le clic simple lance l'action dans les deux états, et les gestes délibérés restent ouverts — dépôt d'un dossier depuis l'Explorateur, « ↗ Déplacer vers la page », réorganisation des pages
- **Ranger la fenêtre repose le verrou** : on ne peut pas oublier de le refermer

### Corrections
- **Quota IA indisponible : la place des jauges dit pourquoi.** Elles disparaissaient sans un mot dès que l'API de quota refusait de répondre, et la raison n'existait que dans le fichier de log. Un texte l'annonce désormais, avec la cause technique au survol et le délai avant la prochaine tentative
- Une lecture de quota **annulée** — masquer puis réafficher la fenêtre — ne consomme plus le créneau de cinq minutes : elle laissait sinon cinq minutes de jauges vides sans aucune trace
- Le journal signale **chaque changement** de cause d'indisponibilité, et le retour à la normale, au lieu d'une seule fois par session
- L'aperçu du raccourci clavier, la liste des touches et le badge de la fenêtre principale affichent tous les trois le même nom de touche

### Notes
- Les messages renvoyés au serveur MCP et ceux du journal restent en **français** : leurs lecteurs sont un modèle et le développeur, pas l'interface
- Les listes de **Prédéfinis** et du **gestionnaire de menu contextuel** ne se retraduisent pas à chaud ; sans conséquence en pratique, la fenêtre Options étant modale
- Les noms que tu as personnalisés — navigateurs, profils, fournisseurs — ne changent jamais de langue : ils t'appartiennent

### Interne
- 58 tests ajoutés (370 au total), dont **quatre gardes de cohérence** vérifiées par mutation : aucun texte littéral dans un XAML, aucun texte français d'interface en dur dans le C#, toute clé citée existe, aucune clé du magasin n'est orpheline
- Magasin RESX (anglais neutre + satellite français) derrière un service `Loc` sans dépendance WPF, ce qui garde services et ViewModels testables sans interface. La bascule à chaud tient à une notification d'indexeur
- `SmartFormat` (cœur seul) pour le pluriel CLDR et les conjonctions de listes
- `tools/DialogShot` capture n'importe quelle fenêtre dans une langue donnée, hors process DockPad

## [1.10.0] — 2026-08-21

### Nouveautés utilisateur

#### Bandeau Usage IA
- Un bandeau sous la grille affiche la **consommation de jetons** de tes assistants de code, lue dans leurs journaux locaux : session, jour, mois, nombre de requêtes, modèle courant
- **Quatre assistants détectés** : Claude Code, Codex, Gemini CLI et Copilot CLI. Un onglet par assistant, avec sa pastille de couleur ; un seul détecté, il n'y a pas d'onglet du tout
- **Deux jauges pour Claude Code** — fenêtre de session (5 h) et fenêtre hebdomadaire — avec le pourcentage consommé et l'heure de remise à zéro. La barre se vide à mesure que tu consommes, comme une jauge de carburant, et passe à l'orange puis au rouge sous le seuil d'alerte
- **Coût estimé** du mois pour Claude Code, dans la devise de la source (dollars, sans conversion). Les trois autres n'exposent aucun tarif public fiable : la colonne affiche un tiret plutôt qu'un montant inventé
- **La pastille à droite ouvre la page d'usage officielle** de l'assistant affiché
- Un **sablier** remplace la pastille pendant la lecture : parcourir les transcripts prend un instant, et les valeurs précédentes restent affichées
- Un assistant **installé mais non utilisé sur la période garde son onglet**, à zéro. Disparaître du bandeau veut dire « pas installé », et rien d'autre
- Une valeur inconnue s'affiche `—`, jamais `0` : une session à zéro veut dire « aucun bloc actif », pas « rien consommé ». Un quota indisponible masque la jauge entière au lieu d'annoncer 0 %

#### Fenêtre « Usage IA »
- ☰ → Paramètres → **📊 Usage IA** : afficher ou masquer le bandeau, seuil d'alerte, afficher le coût, assistant affiché à l'ouverture
- **↻ Redétecter** liste les assistants installés avec leur chemin de données ; chaque ligne se masque sans être perdue. Un assistant masqué n'est **pas interrogé du tout**
- Bandeau désactivé = **au repos** : aucun fichier lu, aucun appel réseau, et le bandeau ne laisse aucun espace vide sous la grille

### Corrections
- La fenêtre d'accès rapide prend désormais la hauteur de son contenu : plus de barre de défilement quand la pagination grandit

### Notes
- Le quota officiel de Claude Code est lu sur un endpoint non documenté, qui cassera un jour : toute panne — jeton absent, expiré, refus, réseau coupé — masque les jauges et conserve les compteurs de jetons, sans jamais afficher d'erreur
- Le jeton d'accès est lu depuis `%USERPROFILE%\.claude\.credentials.json`, gardé en mémoire, et n'est jamais journalisé, recopié dans une config, ni envoyé ailleurs que sur `api.anthropic.com`
- Codex : une session dérivée d'une autre (fork) peut être comptée deux fois, ses événements ne portant aucun identifiant permettant de dédupliquer entre fichiers

### Interne
- 207 tests ajoutés (312 au total) : formatage, tarifs, quota, fusion des fournisseurs, ViewModel, et les quatre lecteurs sur dossiers temporaires et base SQLite de fixture
- `IUsageProvider` porte la détection **et** la lecture : un assistant = un fichier, et `UsageProviderRegistry` est le seul point d'enregistrement. L'arrivée de Codex, Gemini et Copilot n'a touché ni le bandeau ni la fenêtre de réglages
- Déduplication des transcripts Claude sur `(message.id, requestId)` : mesuré 49 % de lignes en double sur 407 fichiers réels, sans quoi tous les totaux sont à peu près doublés
- Quota appelé au plus toutes les 5 minutes, dernière valeur conservée 15 : le bandeau se rafraîchissant chaque minute, il se faisait refuser par l'API (HTTP 429) pour des fenêtres qui durent 5 h et 7 jours
- Nouvelle config `%APPDATA%\DockPad\usage.json`, incluse dans 💾 Sauvegarder la configuration
- Nouvel outil `tools/UsageShot` : capture le bandeau et sa fenêtre de réglages sur des fournisseurs de démonstration — aucune donnée de consommation réelle dans la documentation
- Dépendance `Microsoft.Data.Sqlite` (base de Copilot CLI), alignée sur .NET 8 ; les binaires natifs des plateformes non Windows sont écartés de la publication

---



## [1.9.0] — 2026-08-13

### Nouveautés utilisateur

#### Profils de navigateurs dans la popup de choix
- La popup « Ouvrir avec… » propose désormais les **profils** des navigateurs Chromium (Chrome, Edge, Brave, Vivaldi…) : chaque profil est une ligne indentée sous son navigateur, avec son nom et son image de profil
- Détection par **↻ Redétecter** dans ☰ → Paramètres → 🌐 Navigateurs, en lisant les profils déclarés par le navigateur lui-même. Un navigateur qui n'a **qu'un seul** profil reste une ligne unique : son comportement par défaut suffit
- Le navigateur reste choisissable — il ouvre le dernier profil utilisé — et sert de titre à son groupe. Le masquer en gardant ses profils le réduit à un simple titre
- Chaque profil se masque, se renomme et se réordonne ; ↑/↓ déplacent un navigateur avec tous ses profils, ou un profil au sein de son groupe
- Une **règle de domaine peut viser un profil** : « Toujours pour ce domaine » retient exactement la ligne choisie, et l'onglet Règles libelle les profils `Chrome › Boulot`
- Un profil renommé dans DockPad garde son nom aux redétections suivantes ; renommé dans le navigateur, le nouveau nom remonte
- Un profil supprimé côté navigateur n'est jamais retiré automatiquement (ses règles de domaine seraient perdues) — la suppression reste manuelle
- Firefox n'est pas concerné : seuls les navigateurs Chromium

#### Profil DockPad déplaçable
- La variable d'environnement `DOCKPAD_PROFILE_DIR` choisit le dossier des données (raccourcis, pages, navigateurs, icônes, logs) à la place de `%APPDATA%\DockPad` — profil portable sur clé USB, ou plusieurs configurations de test côte à côte

### Interne
- 62 tests ajoutés (105 au total) : détection et fusion des profils, mise en page des groupes, construction de la ligne de commande de lancement, résolution du dossier de profil
- `dockpad_browser_list` expose `parentId` et `profileDirectory` ; `order` d'un profil se compte parmi les profils de son navigateur
- Nouvel outil `tools/BrowserShot` : capture la popup et la fenêtre Navigateurs pour la documentation, sur un profil de fixture — les captures ne contiennent aucune donnée personnelle et sont reproductibles

---

## [1.8.1] — 2026-08-07

### Corrections

#### Fenêtres de config uniformisées
- **Serveur MCP** : le bas de l'onglet Options était coupé (hauteur fixe trop petite) — hauteur revue, contenu scrollable, et footer ajouté (version + Fermer)
- **Navigateurs** : la fenêtre (760 px) pouvait déborder d'un écran 768p — hauteur désormais plafonnée à la zone de travail
- **Options** : la fenêtre s'ouvrait à une taille arbitraire (aucune taille définie) — elle s'ajuste maintenant à son contenu
- Les quatre fenêtres de config (Options, Navigateurs, Serveur MCP, Prédéfinis) partagent le même pied de page : version de l'application à gauche, boutons à droite

---

## [1.8.0] — 2026-08-07

### Nouveautés utilisateur

#### Serveur MCP — Claude peut gérer DockPad
- DockPad expose un serveur MCP : Claude Code et Claude Desktop peuvent lire la grille, ajouter des raccourcis (unitairement ou en lot), créer/réorganiser des pages et gérer les navigateurs et règles de domaine — la grille se rafraîchit en direct
- **13 outils** `dockpad_<domaine>_<action>` (grid_get, shortcut_add/update/move/delete, page_add/update/delete, browser_list/update, rule_list/add/delete), positions 0-based, erreurs explicites (case occupée → occupant + cases libres)
- Nouvelle fenêtre **☰ → Paramètres → 🔌 Serveur MCP** : activation, autorisation de suppression (désactivée par défaut — Claude peut construire, pas détruire), commandes d'enregistrement prêtes à copier (portée projet ou `-s user`, commande de mise à jour du chemin), et **onglet Journal** listant chaque action MCP (✅ exécutée / 🚫 refusée / ❌ erreur)
- Configuration dans `%APPDATA%\DockPad\mcp.json`, incluse dans 💾 Sauvegarder la configuration ; chaque action est aussi tracée dans les logs
- Enregistrement : `claude mcp add dockpad -- "C:\DockPad\DockPad.exe" --mcp` (DockPad doit être lancé pour que les outils répondent)

#### Corrections
- La duplication d'une tuile `SwitchToProcess` perdait le mode de recherche (`SearchMode`) : une tuile « fenêtre par titre » dupliquée redevenait silencieusement « par nom de processus »

### Interne
- La logique des actions (tuiles, pages, navigateurs, règles) est extraite dans des services partagés UI ↔ MCP, sérialisés par un verrou global (plus d'écrasement possible entre l'app et Claude)
- Nouveau projet de tests `DockPad.Tests` (xUnit, 48 tests) ; `IconCacheService` renommé `IconStoreService` (le dossier `icons\` est un store de référence, pas un cache)

---

## [1.7.0] — 2026-08-06

### Nouveautés utilisateur

#### Journal des erreurs
- Toutes les erreurs sont désormais tracées dans `%APPDATA%\DockPad\logs\dockpad-AAAAMMJJ.log` (un fichier par jour, 14 jours conservés) : exceptions inattendues, erreurs affichées à l'écran (stack trace complète dans le log), et erreurs silencieuses jusqu'ici invisibles (config JSON corrompue, icône illisible, pipe interrompu…)
- Remplace l'ancien `%APPDATA%\DockPad\error.log` (qui n'est plus alimenté)

---

## [1.6.2] — 2026-08-05

### Nouveautés utilisateur

#### Règles de domaine sensibles au port
- Nouvelle capacité : les règles de domaine peuvent maintenant inclure un port (ex : `localhost:44351` et `localhost:44307` sont traités comme distincts) ; la case « Toujours pour ce domaine » inclut le port s'il est non-défaut
- Changement de comportement : une règle sans port ne matche plus une URL à port explicite non-défaut (ex : la règle `github.com` ne matche plus `github.com:8080`)

---

## [1.6.1] — 2026-08-04

### Nouveautés utilisateur

#### Ouverture automatique dans le sélecteur de navigateur
- Nouveau réglage **« Ouverture automatique »** (🌐 Navigateurs, 0 = désactivé, défaut 2 s) : si aucun choix n'est fait au bout de N secondes, l'URL s'ouvre avec le navigateur n°1
- Décompte affiché sur le badge du navigateur n°1 (bleu, « Ns ») + ligne « Ouverture automatique dans N s »
- Toute interaction avec la popup (touche, clic, molette) annule le décompte

#### Corrections
- **Crash à la fermeture de la popup** (Échap/perte de focus) : le handler `Deactivated` rappelait `Close()` pendant la fermeture → exception non gérée, l'application quittait entièrement (plus de raccourci global). Corrigé + filet de sécurité global (`DispatcherUnhandledException`, log dans `%APPDATA%\DockPad\error.log`)
- Boutons ▲ ▼ 🗑 ➕ et « … » du dialog Navigateurs illisibles (réduits à 2 px par le padding hérité)
- Badges de touche de la popup alignés sur le style de l'overlay clavier de la grille
- Icône de Chrome Canary : l'index de la valeur registre `DefaultIcon` (`chrome.exe,4`) est désormais respecté → icône jaune

---

## [1.6.0] — 2026-08-04

### Nouveautés utilisateur

#### Sélecteur de navigateur
- DockPad peut être défini comme navigateur par défaut ; au clic sur une URL, une popup propose le choix du navigateur (Chrome, Canary, Edge…)
- Popup : URL sélectionnable + bouton copier, touches 1-9 / flèches / Échap, case « Toujours pour ce domaine » (ouverture directe ensuite, sous-domaines inclus)
- Configuration ☰ → Paramètres → 🌐 Navigateurs : auto-détection, édition (arguments, profils, navigation privée), masquage, réordonnancement, règles de domaine, enregistrement Windows (per-user, sans admin)
- `browsers.json` inclus dans la sauvegarde de configuration

---

## [1.5.8] — 2026-07-20

### Nouveautés utilisateur

#### Modificateurs des raccourcis de tuiles configurables
- Nouvelle section **« Raccourcis des tuiles »** dans Options : choix du modificateur de chaque moitié de la grille (ex : **Alt + touche** à gauche, **Shift + touche** à droite)
- Choix possibles : Auto (selon le raccourci global, comportement historique), Ctrl, Alt, Shift
- Les deux moitiés doivent utiliser des modificateurs différents ; si une seule est configurée, le mode Auto s'applique

---

## [1.5.7] — 2026-07-20

### Nouveautés utilisateur

#### Overlay clavier — ligne du bas complète
- Les 2 cases restantes de la ligne du bas sont désormais accessibles : **Ctrl + ↑ / ↓** (côté gauche) et **Shift + ↑ / ↓** (côté droit), en plus du **0** existant
- L'overlay affiche les badges `0`, `↑`, `↓` sur la ligne du bas — les 12 tuiles de chaque moitié sont maintenant toutes exécutables au clavier

#### Navigation de page au clavier
- **→** seule : page suivante ; **←** seule : page précédente (sans bouclage aux extrémités)

#### Pavé numérique
- Les raccourcis chiffres fonctionnent désormais aussi avec le **pavé numérique**, y compris avec Shift (qui annule temporairement NumLock sous Windows) ou NumLock éteint
- Les vraies touches fléchées restent distinctes des flèches du pavé numérique

---

## [1.5.6] — 2026-07-20

### Corrections

#### Prédéfini « Ouvrir dans GitHub Desktop »
- La commande référence désormais `%LocalAppData%\GitHubDesktop\GitHubDesktop.exe` (écrit en `REG_EXPAND_SZ`, résolu au clic) au lieu d'un chemin absolu figé : l'entrée fonctionne pour chaque compte Windows, y compris quand l'installation est faite depuis un compte admin élevé différent
- Le prédéfini n'est **proposé que si GitHub Desktop est installé** (version ≥ 3.4.14, requise pour `--cli-open`) — plus de commande morte `GitHubDesktop.exe` écrite dans le registre quand l'application est absente
- Le suffixe `\.` dans `--cli-open="%V\."` corrige l'ouverture depuis la **racine d'un lecteur** (ex : `D:\`), dont le backslash final cassait l'argument transmis
- Le statut du prédéfini converge désormais (plus de « Mise à jour disponible » permanent) : la comparaison lit le registre sans expansion des variables d'environnement

---

## [1.5.5] — 2026-06-22

### Nouveautés utilisateur

#### Nouveau prédéfini « Ouvrir dans GitHub Desktop »
- Nouvelle entrée de menu contextuel sur le fond de dossier : ajoute **et** ouvre le dépôt dans GitHub Desktop
- Utilise le flag interne `GitHubDesktop.exe --cli-open="%V"` — la même commande que le shim `github` (`bin\github.bat` → `cli.js`) finit par exécuter, mais appelée directement
- Aucune **fenêtre console** ne reste ouverte au premier lancement (pas de passage par la chaîne `cmd`/`.bat` du shim, qui bloquait pendant le démarrage à froid)

---

## [1.5.4] — 2026-04-04

### Nouveautés utilisateur

#### Basculer vers un processus — recherche par titre de fenêtre
- Nouveau mode de recherche dans le type de tuile **"Basculer vers un processus"** : **Par titre de fenêtre**
- Permet de cibler des applications dont le nom de processus est difficile à connaître (ex : Calculatrice Windows, apps UWP)
- Exemple : saisir `Calculatrice` pour retrouver et mettre au premier plan la Calculatrice Windows
- La recherche est insensible à la casse et cherche le texte saisi dans le titre de la fenêtre (correspondance partielle)
- Si aucune fenêtre correspondante n'est trouvée, l'exécutable est lancé normalement
- Le champ **Paramètres** reste disponible dans les deux modes (utilisé au lancement si la fenêtre/processus est absent)

---

## [1.5.3] — 2026-03-30

### Nouveautés utilisateur

#### Version affichée dans la fenêtre principale
- Le numéro de version (ex: `v1.5.3`) est affiché en bas à gauche de la fenêtre des raccourcis rapides
- Police `Consolas`, même style que le badge du raccourci clavier

---

## [1.5.2] — 2026-03-29

### Nouveautés utilisateur

#### Drag & drop depuis l'Explorateur Windows
- Glisser-déposer un **dossier** depuis l'Explorateur sur une tuile → crée automatiquement un raccourci "Ouvrir un dossier"
  - Icône dossier par défaut incluse avec le programme (jaune, style Windows)
  - Si la case est vide : raccourci créé immédiatement
  - Si la case est occupée : dialog pré-rempli pour confirmer ou modifier
- Glisser-déposer un **fichier .url** (raccourci Internet) → crée un raccourci "Ouvrir dans le navigateur"
  - L'URL et le titre sont extraits automatiquement du fichier
  - L'icône du navigateur par défaut est détectée et utilisée automatiquement

---

## [1.5.1] — 2026-03-26

### Nouveautés utilisateur

#### Arguments Claude Code configurables
- Champ texte libre dans **Options** (section "Claude Code") pour passer des arguments supplémentaires à Claude Code
- Exemple : `--enable-auto-mode`
- Les arguments sont appliqués au prédéfini "Ouvrir un terminal Claude" (Windows Terminal et PowerShell)
- Stocké dans `HKCU\Software\DockPad\Settings\ClaudeArgs`

---

## [1.5.0] — 2026-03-20

### Nouveautés utilisateur

#### Nouveau type de tuile : Basculer vers un processus
- Si un processus avec les mêmes paramètres est déjà en cours d'exécution, sa fenêtre est mise au premier plan (restaurée si réduite)
- Sinon, le programme est lancé
- Configuration : nom du processus (ex: `devenv.exe`), chemin de l'exécutable, paramètres optionnels
- Le nom du processus est auto-rempli depuis le chemin de l'exécutable
- Bande colorée rouge pastel

#### Icône automatique depuis l'exécutable
- À la création ou modification d'un raccourci, si aucune icône n'est spécifiée, l'icône est extraite automatiquement depuis l'exécutable associé
- Fonctionne pour les types : `RunCommand`, `SwitchToProcess`, `OpenTerminal`

---

## [1.4.0] — 2026-03-20

### Nouveautés utilisateur

#### Barre de recherche globale
- Champ de recherche dans la toolbar : filtre les raccourcis par nom sur toutes les pages
- Résultats dans un popup avec icône, nom et type coloré
- Navigation clavier : **↓** pour entrer dans la liste, **Entrée** pour exécuter, **Échap** pour fermer
- **Retour arrière** depuis la liste remet le focus sur le champ de saisie

#### Raccourcis clavier par chiffre (overlay)
- Depuis la fenêtre principale : appuyer sur **Ctrl** seul affiche un overlay numéroté sur les 3×3 tuiles gauches, **Shift** seul sur les 3×3 tuiles droites
- Maintenir **Ctrl + 1-9** ou **Shift + 1-9** exécute directement la tuile correspondante
- L'overlay se masque à la désactivation de la fenêtre ou à la frappe Escape
- Les modificateurs trigger s'adaptent automatiquement au raccourci global configuré

#### Cache d'icônes (profil portable)
- Les icônes sont automatiquement copiées dans `%APPDATA%\DockPad\icons\` lors de la sauvegarde d'un raccourci ou d'une icône de page
- Les icônes `.exe`/`.dll` sont extraites et sauvegardées en `.png`
- Déduplication par hash SHA1 : pas de doublon dans le cache
- Si le fichier source d'icône n'existe plus, le cache profil est utilisé en fallback
- Bouton **↻ Actualiser** : synchronise le cache profil pour toutes les entrées existantes

---

## [1.3.0] — 2026-03-20

### Nouveautés utilisateur

#### Toolbar
- Ajout d'un bouton *─* pour réduire la fenêtre
- Le bouton *✕* masque désormais la fenêtre dans la barre système (au lieu de quitter)

#### Raccourci clavier
- Le raccourci clavier configuré est affiché en permanence en bas à droite de la fenêtre

#### Menu ☰
- Le menu est organisé en trois sections :
  - *Menu contextuel* : Gestion, Raccourcis prédéfinis
  - *Paramètres* : Options
  - *Configuration* : Actualiser, Modifier, Sauvegarder, Voir le dossier
  - Quitter l'application

#### Menu configuration
- Nouvelle action *Voir le dossier* ouvre directement %APPDATA%\DockPad\ dans l'Explorateur
- Nouvelle action *Sauvegarder*

#### Paramètres
- La version de l'application est affichée dans le footer du dialog Paramètres
- 

---

---

# Changelog technique (Architecture)

## [1.5.8]

- `Services/SettingsService.cs` — `LoadTriggerMods`/`SaveTriggerMods` (`TriggerFirst`/`TriggerSecond`, `""` = auto)
- `Views/QuickAccessWindow.xaml.cs` — `UpdateTriggerMods` : config explicite prioritaire (`ParseTriggerMod`), sinon auto selon le hotkey global
- `Dialogs/SettingsDialog.xaml/.cs` — section « Raccourcis des tuiles » : deux ComboBox (Auto/Ctrl/Alt/Shift), validation `ValidateTriggers` (différents, paire complète sinon auto)

## [1.5.7]

- `Views/QuickAccessWindow.xaml.cs` — `GetHintDigit` → `GetHintKey` (0-9, ↑=10, ↓=11) avec remap des touches de navigation non-étendues du pavé numérique ; flag étendu (bit 24 du lParam) lu sur le message en cours via `ComponentDispatcher.CurrentKeyboardMessage` ; `HintKeyToCell` étendu (row 3 : 0/↑/↓) ; `ShowHintOverlay` badge la ligne du bas complète ; `GoToAdjacentPage` (←/→ seules) ; touche effective via `SystemKey` (trigger Alt / fenêtre sans focus clavier)

## [1.5.6]

- `Services/PresetService.cs` — `GetPresets()` filtre les prédéfinis `null` (non disponibles) ; `BuildGitHubDesktop()` retourne `null` si l'exe est absent ou < 3.4.14 (`FileVersionInfo`), commande/icône en `%LocalAppData%` littéral ; nouveau helper `BuildFolderPreset` factorise `BuildVSCode`/`BuildSSMS`
- `Services/RegistryService.cs` — `Save` écrit commande/icône en `REG_EXPAND_SZ` quand la valeur contient `%Var%` (`GetValueKind`) ; `GetValues` lit avec `RegistryValueOptions.DoNotExpandEnvironmentNames` pour que la comparaison des prédéfinis porte sur les valeurs brutes (`LoadForTarget` reste expansé : affichage + exécution)

## [1.5.0]

- `Models/ProcessSwitchConfig.cs` — nouveau modèle : `ProcessName`, `Executable`, `Parameters`
- `Models/ShortcutEntry.cs` — ajout `SwitchToProcess` dans l'enum + champ `ProcessSwitch`
- `Services/ProcessSwitchService.cs` — `SwitchOrLaunch` : recherche via WMI (`Win32_Process.CommandLine`), `SetForegroundWindow` + `ShowWindow(SW_RESTORE)` si minimisé, sinon lance l'exe
- `Dialogs/ShortcutDialog.xaml` — nouveau `ComboBoxItem` + `PanelProcessSwitch` (exe, processName, paramètres)
- `Dialogs/ShortcutDialog.xaml.cs` — gestion du panel, auto-fill `ProcessName` depuis l'exe, `TryAutoFillIcon` (icône auto depuis l'exe si aucune icône spécifiée), `ParseExe`
- `Views/QuickAccessWindow.xaml.cs` — bande rouge pastel `BandSwitchToProcess`, label "Processus", exécution via `ProcessSwitchService`, copie `ProcessSwitch` dans `EditTile` et `DuplicateTile`
- `DockPad.csproj` — ajout `System.Management 8.0.0`

## [1.4.0]

- `Services/IconCacheService.cs` — nouveau service : `CopyToProfile` (copie avec dédup SHA1), `ResolveProfilePath`, `SyncAll`, `SyncAllPages` ; extraction .exe/.dll → .png via `System.Drawing`
- `Models/ShortcutEntry.cs` — ajout `IconProfilePath` (`[JsonIgnore(WhenWritingNull)]`)
- `Models/PageConfig.cs` — ajout `IconProfilePath` + `using System.Text.Json.Serialization`
- `Dialogs/ShortcutDialog.xaml.cs` — copie `IconProfilePath` depuis `existing` ; affichage du chemin profil si IconPath manquant ; `GetIconInitialDir` ; sauvegarde via `IconCacheService.CopyToProfile`
- `Views/QuickAccessWindow.xaml.cs` — overlay hints (`ShowHintOverlay`, `HideHintOverlay`, `_hintElements`, `_hintIsCtrl`) ; `UpdateTriggerMods` (adapte Ctrl/Shift selon hotkey) ; `OnPreviewKeyDown/Up` pour exécution par chiffre ; chargement icônes via `IconCacheService.ResolveProfilePath` partout ; `Refresh_Click` avec `SyncAll`/`SyncAllPages` ; `ClearPageIcon` remplace `SetPageIcon("")`

## [1.3.0]

- `App.xaml` — styles globaux `ContextMenu`, `MenuItem`, `Separator` : fond blanc, `CornerRadius=6`, `DropShadowEffect`, hover `#EEF6FC`, en-têtes de groupe grisés
- `QuickAccessWindow.xaml` — toolbar simplifiée : boutons `─` (réduire) et `⬇` (masquer systray) ; barre de pagination convertie en `Grid` pour le badge hotkey ; menu ☰ inline avec trois sections
- `QuickAccessWindow.xaml.cs` — ajout de `Minimize_Click`, `HideToSystray_Click`, `UpdateHotkeyDisplay()`, `BackupConfig_Click`, `OpenConfigFolder_Click`, `OpenContextMenuManager_Click`, `Menu_Click` ; appel de `UpdateHotkeyDisplay()` au chargement et après sauvegarde des paramètres
- Sauvegarde : copie horodatée (`yyyyMMdd_HHmmss`) de `shortcuts.json` et `pages.json` dans `%APPDATA%\DockPad\.backup\`
- `SettingsDialog.xaml` — footer converti en `Grid` avec `TextBlock` version à gauche et boutons à droite
- `SettingsDialog.xaml.cs` — version lue via `Assembly.GetExecutingAssembly().GetName().Version`
- `ContextMenuManagerWindow.xaml` — `Icon="app.ico"` → `Icon="pack://application:,,,/app.ico"` (fix résolution depuis sous-dossier `Views/`)
- `DockPad.csproj` — `<Version>1.3.0</Version>` ; target MSBuild `ZipAfterPublish` : crée `release\DockPad-{version}.zip` puis supprime `publish\` via `<RemoveDir>`
- `Properties/PublishProfiles/FolderProfile.pubxml` — profil publish framework-dependent, `PublishDir` à la racine dans `publish\`
