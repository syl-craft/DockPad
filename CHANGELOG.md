# Changelog

## [1.6.1] — 2026-08-04

### Nouveautés utilisateur

#### Ouverture automatique dans le sélecteur de navigateur
- Nouveau réglage **« Ouverture automatique »** (🌐 Navigateurs, 0 = désactivé) : si aucun choix n'est fait au bout de N secondes, l'URL s'ouvre avec le navigateur n°1
- Décompte affiché sur le badge du navigateur n°1 (bleu, « Ns ») + ligne « Ouverture automatique dans N s »
- Toute interaction avec la popup (touche, clic, molette) annule le décompte

#### Corrections d'interface
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
