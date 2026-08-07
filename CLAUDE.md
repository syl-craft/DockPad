# DockPad

Application WPF C# (.NET 8, x64) pour gérer le menu clic droit Windows via le registre.
L'app démarre sans droits admin — l'élévation est demandée à la demande via un bouton UAC.

## Stack

- **WPF / .NET 8**  `net8.0-windows`, `UseWPF=true`, `UseWindowsForms=true` (pour NotifyIcon)
- **Registre**  lecture/écriture via `Microsoft.Win32.Registry`
- **Icônes**  `System.Drawing.Common` (NuGet) pour extraire les icônes `.exe`/`.dll`
- **JSON**  `System.Text.Json` (built-in) pour la config des raccourcis rapides
- **WMI**  `System.Management` (NuGet) pour lire la ligne de commande des processus (`SwitchToProcess`)
- **Logs**  Serilog + Serilog.Sinks.File — logger central `LogService`

## Structure

```
App.xaml/.cs                             Point d'entrée : instance unique (Mutex), NotifyIcon systray
GlobalUsings.cs                          Usings globaux
DockPad.csproj / DockPad.sln

Assets/
    folder.png                           Icône dossier par défaut (jaune, style Windows 11) — ressource embarquée

Converters/
    InverseBoolConverter.cs              Converter WPF bool inversé

Models/
    ActionResult.cs                       Résultat d'une action des services (UI/MCP) : Ok + Data, ou échec + Error
    BrowserEntry.cs                       Modèle navigateur (id, name, exePath, arguments, icône, hidden, order)
    BrowserRule.cs                        Règle domaine → navigateur (host exact + sous-domaines)
    BrowsersConfig.cs                     Contenu de browsers.json (liste navigateurs + règles)
    BrowserUpdate.cs                      Champs modifiables par dockpad_browser_update (null = inchangé)
    ContextMenuEntry.cs                  Modèle de données + enum ContextMenuTarget
    ContextMenuEntryViewModel.cs         VM avec chargement d'icône (BitmapSource)
    McpConfig.cs                          Contenu de mcp.json (Enabled, AllowDelete)
    PageConfig.cs                        Config par page (icône du bouton de pagination + IconProfilePath)
    PresetEntry.cs                       Modèle preset avec enum PresetStatus
    ProcessSwitchConfig.cs               Config SwitchToProcess (processName, executable, parameters)
    ShortcutAddItem.cs                    Item du lot dockpad_shortcut_add (position optionnelle)
    ShortcutEntry.cs                     Modèle raccourci rapide (page, row, col, name, type, command, iconPath, iconProfilePath)
    ShortcutUpdate.cs                     Champs modifiables par dockpad_shortcut_update (null = inchangé)
    TerminalConfig.cs                    Config d'un terminal (exePath, startingDirectory, runCommand…)
    TerminalInfo.cs                      Informations d'un terminal détecté

Services/
    BrowserActionService.cs               Actions navigateurs & règles de domaine, partagées UI ↔ MCP
    BrowserConfigService.cs              Load/Save browsers.json (%APPDATA%\DockPad\browsers.json)
    BrowserDetectionService.cs           Détection des navigateurs installés (Software\Clients\StartMenuInternet, HKLM+HKCU)
    BrowserRegistrationService.cs        Enregistrement per-user (HKCU) comme navigateur + lecture de l'état (non enregistré/enregistré/par défaut)
    ConfigLock.cs                         Verrou global des load-modify-save de configs (UI et MCP sérialisés)
    HotkeyService.cs                     P/Invoke RegisterHotKey / UnregisterHotKey (user32.dll)
    IconCacheService.cs                  Cache d'icônes dans %APPDATA%\DockPad\icons\ (SHA1 dédup, extraction .exe/.dll → .png)
    LogService.cs                        Logger central Serilog — %APPDATA%\DockPad\logs\, rolling quotidien, 14 fichiers, shared multi-process
    McpConfigService.cs                   Load/Save mcp.json (%APPDATA%\DockPad\mcp.json)
    McpDispatcher.cs                       Traite une requête MCP : options → service d'action → journal → réponse JSON
    McpLogService.cs                       Journal en mémoire des actions MCP de la session (onglet Journal)
    McpPipeService.cs                      Named pipe DockPad_McpPipe — serveur multi-instances (instance principale) / client (relais --mcp)
    PageActionService.cs                  Actions sur les pages, partagées UI ↔ MCP (mêmes règles que la pagination)
    PageConfigService.cs                 Load/Save pages.json (%APPDATA%\DockPad\pages.json)
    PresetService.cs                     Raccourcis prédéfinis (Claude, PowerShell, VS Code, SSMS, GitHub Desktop)
    ProcessSwitchService.cs              SwitchOrLaunch : cherche via WMI, SetForegroundWindow ou lance l'exe
    RegistryService.cs                   CRUD registre (HKCR / HKCU / HKLM)
    ResourceStringResolver.cs            Résolution des @dll,-id via SHLoadIndirectString
    SettingsService.cs                   Lecture/écriture paramètres HKCU + autostart
    ShortcutActionService.cs              Actions sur la grille de raccourcis, partagées UI ↔ MCP (cœurs purs + enveloppes verrou/IO)
    ShortcutService.cs                   Load/Save shortcuts.json (%APPDATA%\DockPad\shortcuts.json)
    TerminalDetectionService.cs          Détection des terminaux installés + construction des arguments
    UrlPipeService.cs                    Named pipe DockPad_UrlPipe — serveur (instance principale) / client (instance relais)
    UrlRouterService.cs                  Règles de domaine, file d'URLs, orchestration popup/lancement

Mcp/
    DockPadTools.cs                        Les 13 outils dockpad_* exposés au SDK MCP (relais vers le pipe)
    McpRelay.cs                            Hôte MCP stdio du mode --mcp (SDK ModelContextProtocol, aucune UI/mutex)

Views/
    ContextMenuManagerWindow.xaml/.cs    Gestion des entrées de menu contextuel Windows
    QuickAccessWindow.xaml/.cs           Grille de tuiles multi-pages (hotkey global)

Dialogs/
    AppDialog.xaml/.cs                   Dialog custom styled (remplace MessageBox) — Confirm/Error/Warning/Info
    BrowserConfigDialog.xaml/.cs         Configuration navigateurs (détection/édition/règles/enregistrement)
    BrowserPickerWindow.xaml/.cs         Popup de choix du navigateur au clic sur une URL
    EntryDialog.xaml/.cs                 Ajout/modification d'une entrée de menu contextuel (registre)
    McpConfigDialog.xaml/.cs               Fenêtre « Serveur MCP » : options (activé/suppression) + journal de session
    PresetsDialog.xaml/.cs               Raccourcis prédéfinis
    SettingsDialog.xaml/.cs              Configuration du raccourci clavier global + démarrage auto + version
    ShortcutDialog.xaml/.cs              Ajout/modification d'une tuile d'accès rapide

DockPad.Tests/                           Projet xUnit (43 tests) : ActionResult/McpConfig/services d'actions/McpLogService/McpDispatcher

tools/
    get-startmenu-apps.ps1               Script PowerShell : résout les AppID Start Menu en chemins .exe
    inject-startmenu-shortcuts.ps1       Script PowerShell : injecte des raccourcis SwitchToProcess dans shortcuts.json
```

## Fonctionnalités

### Icône systray
- L'app tourne en arrière-plan avec une icône dans la barre système
- **Fermeture de la fenêtre** (croix) → masque la fenêtre, l'app continue de tourner
- **Clic gauche** sur l'icône → réaffiche la fenêtre
- **Clic droit** sur l'icône → menu contextuel avec "Fermer" pour quitter vraiment
- **Instance unique** : un `Mutex` nommé empêche le lancement de plusieurs instances simultanées

### Logging (LogService)
- Serilog → `%APPDATA%\DockPad\logs\dockpad-YYYYMMDD.log`, rolling quotidien, 14 fichiers gardés, `shared: true` (l'instance relais URL écrit dans le même fichier)
- `MinimumLevel` : Information (permet le niveau Info ci-dessous)
- Format : `[2026-08-06 14:23:45.123 ERR] contexte` + stack trace complète
- **Error** : exceptions non gérées (Dispatcher, AppDomain, TaskScheduler) et catch affichant un `AppDialog.Error`
- **Warning** : catch silencieux (configs JSON corrompues, icônes, pipe…) — comportement utilisateur inchangé
- **Info** : actions MCP (`McpLogService.Add` trace chaque appel d'outil — succès/refus/erreur — dans les logs en plus du journal en mémoire)
- Pas de log : UAC refusé, boucle WMI de `ProcessSwitchService` (refus d'accès routiniers)
- API : `LogService.Error(ex, "contexte")` / `LogService.Warn(ex, "contexte")` / `LogService.Info("message")` — jamais `Serilog` en direct

### Gestionnaire de menu contextuel (ContextMenuManagerWindow)
- Liste les entrées de menu contextuel (Fichiers, Dossiers, Fond de dossier uniquement)
- Filtre les entrées sans commande et avec `LegacyDisable`
- Résout les noms `@shell32.dll,-xxxx` en texte lisible
- Retire les `&` (caractères de raccourci clavier Windows)
- Ajout / Modification / Suppression / Duplication
- **Suppression** : pose `LegacyDisable` dans HKCU pour les entrées HKLM, suppression directe pour HKCU
- Auto-resize des colonnes après chaque chargement
- **Élévation à la demande** : bouton `🛡 Élever` + bandeau jaune si non-admin, relance l'app en admin via UAC

### Raccourcis prédéfinis (PresetsDialog)
- Ouvert depuis le bouton **Prédéfinis** de `QuickAccessWindow`
- Détecte et propose l'installation / mise à jour des prédéfinis (comparaison commande + icône)
- **Non-admin** : bouton "Installer" remplacé par `🛡 Élever` — relance en admin via UAC
- Bouton **☰ Menu contextuel** → ouvre `ContextMenuManagerWindow` directement depuis le dialog
- Bouton **↻ Actualiser** → relit l'état du registre sans fermer le dialog

### Accès rapide (QuickAccessWindow)
- Grille **4 lignes × 6 colonnes** de tuiles, sur plusieurs **pages**
- Pagination en bas : boutons numérotés ou avec icône, bouton `+` pour ajouter une page
- Chaque tuile : icône + nom → exécute l'action selon son **type**
- Bande colorée (4px, droite) indique le type : bleu=RunCommand, ambre=OpenFolder, vert=OpenUrl, violet=OpenTerminal, rouge=SwitchToProcess
- Icônes supportées : `.exe`, `.dll`, `.ico`, `.png`, `.bmp`, `.jpg`
- Cases vides affichées en `+` grisé
- Fenêtre sans barre Windows (`WindowStyle=None`), déplaçable par drag, icône `app.ico`
- Toolbar : **☰ Menu** (déroulant) | **─** (réduire) | **⬇** (masquer dans la barre système)
- **Menu ☰** organisé en sections :
  - *Menu contextuel* : ☰ Gestion, 📋 Raccourcis prédéfinis
  - *Paramètres* : ⚙ Options, 🌐 Navigateurs
  - *Configuration* : ↺ Actualiser, ✎ Modifier, 💾 Sauvegarder, 📁 Voir le dossier
  - ✕ Quitter l'application
- **Raccourci clavier actif** affiché en bas à droite (badge `Consolas`, mis à jour après changement dans Options)
- **Sauvegarder la configuration** : copie `shortcuts.json`, `pages.json` et `browsers.json` dans `%APPDATA%\DockPad\.backup\` avec horodatage
- Config stockée dans `%APPDATA%\DockPad\shortcuts.json`
- Config pages stockée dans `%APPDATA%\DockPad\pages.json`
- **Clic droit sur une tuile** : 🖼 Changer l'icône | ✏ Modifier | ⧉ Dupliquer | ↗ Déplacer vers la page | 🗑 Supprimer
- **Clic droit sur une tuile OpenFolder** : section supplémentaire avec les entrées `Directory\Background\shell` du registre (substitution `%V` → chemin du dossier)
- **Clic droit sur une case vide** : ➕ Ajouter
- **Clic droit sur un bouton de page** : 🖼 Changer l'icône | ← / → Déplacer | 🗑 Supprimer la page
- **Drag & drop** entre tuiles pour les réorganiser
- **Drag & drop depuis l'Explorateur** : glisser un dossier → raccourci `OpenFolder` (icône dossier par défaut `Assets/folder.png`) ; glisser un `.url` → raccourci `OpenUrl` (icône navigateur par défaut détectée via registre)
- **Déplacer vers la page** : place à la même position si libre, sinon première case disponible ; grisé seulement si la page est pleine

### Barre de recherche globale
- Champ de recherche dans la toolbar : filtre les raccourcis par nom sur toutes les pages
- Résultats dans un popup avec icône, nom et type coloré
- Navigation clavier : **↓** pour entrer dans la liste, **Entrée** pour exécuter, **Échap** pour fermer, **Retour arrière** depuis la liste → focus sur le champ

### Raccourcis clavier par touche (overlay)
- Appuyer sur **Ctrl** seul → overlay sur la moitié gauche (4×3) ; **Shift** seul → moitié droite
- **1-9** : les 3×3 tuiles du haut (lecture gauche→droite, haut→bas) ; ligne du bas : **0**, **↑**, **↓**
- **Ctrl/Shift + touche** exécute directement la tuile correspondante
- **Pavé numérique** supporté : les touches de navigation non-étendues (Shift qui annule NumLock, ou NumLock éteint) sont remappées en chiffres via le bit 24 du lParam, lu sur le message en cours avec `ComponentDispatcher.CurrentKeyboardMessage` (un hook WndProc/ThreadPreprocessMessage ne marche PAS : les touches gérées par WPF n'atteignent pas le WndProc, et HwndSource traite le clavier avant les handlers ThreadPreprocessMessage abonnés après lui) ; les vraies flèches (étendues) gardent leur rôle ↑/↓
- **← / → seules** (sans modificateur) : page précédente / suivante (pas de bouclage aux extrémités)
- L'overlay se masque à la désactivation de la fenêtre ou au relâchement du modificateur
- **Modificateurs configurables** dans Options (section « Raccourcis des tuiles ») : Ctrl / Alt / Shift par moitié, stockés dans `HKCU\Software\DockPad\Settings\TriggerFirst|TriggerSecond` (`""` = Auto) ; les deux doivent différer, sinon mode Auto
- Mode Auto (défaut) : les triggers s'adaptent au raccourci global configuré (hotkey avec Ctrl → Shift/Alt, sinon Ctrl/Shift) ; trigger Alt : touches lues via `SystemKey`

### Cache d'icônes (IconCacheService)
- Les icônes sont copiées dans `%APPDATA%\DockPad\icons\` à la sauvegarde (déduplication SHA1)
- Les `.exe`/`.dll` sont extraits et sauvegardés en `.png`
- `IconProfilePath` (chemin relatif au profil) est prioritaire sur `IconPath` (chemin absolu source)
- À la création/modification : si aucune icône spécifiée, l'icône de l'exe associé est utilisée automatiquement (RunCommand, SwitchToProcess, OpenTerminal)
- **↻ Actualiser** : synchronise le cache pour toutes les entrées existantes

### Types de tuiles (ShortcutType)
| Type | Description | Champ `command` | Bande |
|------|-------------|-----------------|-------|
| `RunCommand` | Lance un exécutable ou une commande shell | ex: `notepad.exe`, `"C:\app.exe" arg` | bleu |
| `OpenFolder` | Ouvre un dossier dans l'Explorateur | chemin du dossier | ambre |
| `OpenUrl` | Ouvre une URL dans le navigateur par défaut | URL complète | vert |
| `OpenTerminal` | Ouvre un terminal dans un dossier (wt → pwsh → powershell → cmd) | chemin du dossier | violet |
| `SwitchToProcess` | Bascule vers un processus existant (même cmdline) ou le lance | `processName args` (tooltip) | rouge |

### SwitchToProcess
Deux modes de recherche configurables via `ProcessSwitchConfig.SearchMode` (`ProcessSearchMode`) :

**`ByProcessName`** (défaut) :
- Cherche un processus par nom via `Process.GetProcessesByName`
- Lit la ligne de commande via WMI (`Win32_Process.CommandLine`) pour matcher les paramètres
- Si trouvé : `SetForegroundWindow` + `ShowWindow(SW_RESTORE)` si minimisé
- Si non trouvé : lance `Executable` avec `Parameters`

**`ByWindowTitle`** :
- Énumère toutes les fenêtres visibles via `EnumWindows` + `GetWindowText` (P/Invoke)
- Cherche le fragment de texte dans le titre (insensible à la casse, correspondance partielle)
- Utile pour les apps UWP dont le nom de processus est opaque (ex : Calculatrice → titre `Calculatrice`)
- Si aucune fenêtre trouvée : lance `Executable` avec `Parameters`
- Champ titre vide → `IntPtr.Zero` directement, pas d'énumération

Config stockée dans `ShortcutEntry.ProcessSwitch` (`ProcessSwitchConfig`)  
Rétrocompatible JSON : `SearchMode` absent → `ByProcessName` par défaut

### Raccourci clavier global
- Hotkey configurable via `SettingsDialog` (Ctrl/Alt/Shift/Win + touche A-Z ou F1-F12)
- Défaut : `Ctrl+Shift+M`
- Affiche `QuickAccessWindow` (la remet au premier plan si déjà visible)
- Config stockée dans `HKCU\Software\DockPad\Settings`
- Enregistrement géré par `QuickAccessWindow`

### Paramètres (SettingsDialog)
- Configuration du raccourci clavier global
- **Raccourcis des tuiles** : choix des modificateurs gauche/droite de l'overlay (Auto, Ctrl, Alt, Shift) avec validation (modificateurs différents)
- **Démarrer avec Windows** : checkbox qui ajoute/supprime une entrée dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- Affiche le chemin de l'exécutable utilisé pour la clé de démarrage automatique
- Affiche la version de l'application (ex: `v1.5.1`) en bas à gauche du footer, lue depuis `Assembly.GetExecutingAssembly()`
- **Claude Code — Arguments supplémentaires** : champ texte libre pour passer des options à `claude` (ex: `--enable-auto-mode`), stocké dans `HKCU\Software\DockPad\Settings\ClaudeArgs`, appliqué au prédéfini "Ouvrir un terminal Claude"

### Sélecteur de navigateur
- DockPad peut être enregistré comme navigateur Windows (per-user, `HKCU`, sans admin) : au clic sur une URL n'importe où dans Windows, une popup propose le choix du navigateur (Chrome, Canary, Edge…) au lieu d'ouvrir directement le navigateur par défaut
- **Enregistrement** (`BrowserRegistrationService`) : `HKCU\Software\DockPad\Capabilities` (`ApplicationName`, `URLAssociations` http/https → ProgID `DockPadURL`) + `HKCU\Software\RegisteredApplications` + `HKCU\Software\Classes\DockPadURL` (`DefaultIcon`, `shell\open\command` = `"<DockPad.exe>" --url "%1"`)
- Windows interdit de définir le navigateur par défaut par programme (hash `UserChoice`) : bouton **« S'enregistrer comme navigateur »** puis bouton ouvrant `ms-settings:defaultapps` pour le choix manuel (une seule fois) ; état affiché : *non enregistré* / *enregistré* / *navigateur par défaut* — si l'exe a été déplacé depuis l'enregistrement, l'état repasse à *non enregistré*
- **Flux au clic sur une URL** : Windows lance `DockPad.exe --url "…"` → instance déjà en fond (mutex non acquis) : l'URL est relayée via le named pipe `DockPad_UrlPipe` (`UrlPipeService`) puis l'instance relais se termine ; DockPad non lancé (mutex acquis) : démarrage en arrière-plan (systray créé, `QuickAccessWindow` non affichée) puis traitement local ; si le pipe est indisponible/timeout (~2 s), fallback : la nouvelle instance affiche elle-même la popup puis quitte
- **Routage** (`UrlRouterService`) : extraction de la clé `host[:port]` (port omis s'il est le port par défaut du scheme) ; si une règle correspond (host exact **ou sous-domaine**, **à port identique** — la règle `github.com` matche `gist.github.com` mais pas `github.com:8080` ; la règle `localhost:44351` ne matche que ce port) → lancement direct sans popup ; sinon affichage de `BrowserPickerWindow` ; les URLs reçues pendant qu'une popup est déjà ouverte sont mises en file et traitées à sa fermeture
- **Popup (`BrowserPickerWindow`)** : fenêtre centrée écran, style DockPad (`WindowStyle=None`, `Topmost`, `ShowInTaskbar=False`) ; URL affichée dans une `TextBox` lecture seule sélectionnable (wrap + scroll) + bouton **⧉ Copier le lien** (style `SecondaryButton`, feedback « Copié ✓ » 1,5 s) ; liste verticale (icône 24px + nom + badge touche au même style que l'overlay clavier de la grille : carré 20px `#BB555555`, chiffre blanc) ; clavier : **1-9** ouverture directe, **↑/↓ + Entrée** navigation, **Échap** annule ; case **« Toujours pour ce domaine »** → crée la règle `host → navigateur` avant l'ouverture ; **perte de focus** (`Deactivated`) ferme sans ouvrir, sauf pendant l'affichage d'un `AppDialog` d'erreur (flag `_suppressClose`, ex : exe navigateur introuvable)
- **Ouverture automatique** : si `autoOpenSeconds > 0` (0 = désactivé ; **défaut 2 s**), un `DispatcherTimer` 1 s décompte à l'ouverture de la popup — badge du navigateur n°1 en bleu accent avec « Ns » + ligne pied « Ouverture automatique dans N s » ; à l'échéance, ouverture avec le navigateur n°1 ; **première interaction** (`PreviewKeyDown`/`PreviewMouseDown`/`PreviewMouseWheel`) → décompte annulé, badge redevient « 1 » ; les items de liste sont des `PickerItem` `INotifyPropertyChanged` (Badge, IsCountdown) pour la mise à jour dynamique
- **Lancement** (`UrlRouterService.Launch`) : `Process.Start` avec l'URL entre guillemets en fin d'arguments ; si `Arguments` contient `%1`, il est substitué par l'URL à la place
- **Configuration (`BrowserConfigDialog`)** : ☰ → Paramètres → 🌐 Navigateurs — fenêtre 680×760 redimensionnable, **2 onglets** (`TabControl` plat, soulignement bleu sur l'onglet actif)
  - **Onglet Navigateurs** : section enregistrement (état + 2 boutons + champ **« Ouverture automatique : N secondes »**, clamp 0-300, sauvegarde immédiate) ; auto-détection (`BrowserDetectionService`, parcours `Software\Clients\StartMenuInternet` HKLM puis HKCU, DockPad exclu, doublons ignorés, **icône lue depuis la valeur `DefaultIcon` avec son index** — ex. Chrome Canary = `chrome.exe,4` pour l'icône jaune) au premier chargement ou si la liste est vide (fichier corrompu) et via **↻ Redétecter** ; **case à cocher de visibilité par ligne** (décochée = absent de la popup, badge « masqué », conservé) ; édition (nom, chemin exe, arguments — ex. `--profile-directory="Profile 1"`, `--incognito`, `-inprivate`), monter/descendre, supprimer, **+ Ajouter**
  - **Onglet Règles de domaine** : recherche live sur le host + filtre par navigateur (combinables), ComboBox par ligne pour réassocier le navigateur (sauvegarde immédiate), suppression par ligne, compteur « N / M règle(s) », état vide explicite ; création uniquement depuis la popup ; supprimer un navigateur supprime ses règles associées
  - Rechargement croisé : si le picker sauvegarde une règle pendant que le dialog est ouvert, `Activated` + comparaison `File.GetLastWriteTimeUtc` rechargent le snapshot
  - **Attention** : les boutons carrés (34px) doivent avoir `Padding="0"` — le `Padding 16,8` hérité du style `PrimaryButton` ne laisse que 2px au glyphe (boutons invisibles)
- Icônes chargées via `LoadIcon` (même pattern dans `BrowserPickerWindow` et `BrowserConfigDialog`) : extraction `.exe`/`.dll` via `System.Drawing.Icon.ExtractAssociatedIcon` puis `DeleteObject` sur le handle GDI (anti-fuite mémoire) ; `IconCacheService.ParseIconRef` découpe `chemin[,index]` et `Icon.ExtractIcon` respecte l'index (négatif = ID de ressource)
- Config stockée dans `%APPDATA%\DockPad\browsers.json`, incluse dans **💾 Sauvegarder la configuration**

### Serveur MCP
- DockPad expose un serveur MCP permettant à Claude Code / Claude Desktop de piloter la grille, les pages et les navigateurs
- **Architecture** : Claude lance `DockPad.exe --mcp` — mode relais stdio (SDK officiel `ModelContextProtocol`), **aucune UI ni mutex**, détecté dans `App.xaml.cs` avant l'acquisition du mutex → chaque appel d'outil sérialise `{tool, args}` en JSON et l'envoie sur le named pipe `DockPad_McpPipe` (`McpPipeService`, multi-instances : Claude Code + Claude Desktop simultanés) → l'instance principale (déjà lancée par l'utilisateur) reçoit la requête : vérifie les options (`mcp.json`), exécute via les services d'actions **partagés avec l'UI** (`ShortcutActionService`, `PageActionService`, `BrowserActionService`), journalise (`McpLogService`), déclenche `RefreshGrid()` sur la grille si mutation, puis répond `{ok, data, error}` en une ligne
- **DockPad doit être lancé** — sinon le pipe est injoignable et l'outil renvoie une erreur explicite (« DockPad n'est pas lancé — démarre l'application pour utiliser ce serveur MCP »)
- **13 outils** `dockpad_<domaine>_<action>` (`Mcp/DockPadTools.cs`), positions **0-based** (page 0 = première page, lignes 0-3, colonnes 0-5) :
  - Grille : `grid_get`, `shortcut_add` (lot tout-ou-rien, position omise = première case libre), `shortcut_update`, `shortcut_move`, `shortcut_delete` 🔒
  - Pages : `page_add`, `page_update` (`iconPath` omis = inchangé, `""` = retirer l'icône ; `newIndex` = déplacement par insertion), `page_delete` 🔒
  - Navigateurs & règles : `browser_list`, `browser_update`, `rule_list`, `rule_add`, `rule_delete` 🔒
  - 🔒 = refusé si `AllowDelete` est désactivé dans `mcp.json`
- **Config `%APPDATA%\DockPad\mcp.json`** (`McpConfigService`) : `{ "enabled": true, "allowDelete": false }` par défaut (suppression refusée par défaut) ; relu à **chaque requête** (changement pris en compte immédiatement, pas besoin de redémarrer) ; incluse dans **💾 Sauvegarder la configuration**
- **Fenêtre `McpConfigDialog`** (☰ → Paramètres → **🔌 Serveur MCP**) : 2 onglets
  - **Options** : case « Serveur activé », case « Autoriser la suppression », commandes d'enregistrement avec bouton ⧉ Copier — Claude Code : `claude mcp add dockpad -- "<chemin réel du .exe>" --mcp` ; Claude Desktop : bloc JSON pour `claude_desktop_config.json`
  - **Journal** : actions de la session en mémoire (`McpLogService.Entries`), icône ✅/🚫/❌ + heure + outil + résumé des paramètres + message, compteur, bouton effacer ; trace persistante en parallèle via `LogService.Info` dans les logs Serilog
- Erreurs renvoyées à Claude en français et actionnables (ex : case occupée → liste des cases libres)

## Prédéfinis

| Nom | Cible | Commande |
| ----- | ------- | --------- |
| Ouvrir un terminal Claude | FolderBackground | `wt.exe -w 0 new-tab --startingDirectory "%V" -- claude` |
| Ouvrir dans PowerShell | FolderBackground | `wt.exe -w 0 new-tab --startingDirectory "%V"` (pwsh/powershell fallback) |
| Ouvrir dans Visual Studio Code | FolderBackground | `code "%V"` |
| Ouvrir dans SQL Server Management Studio | FolderBackground | `ssms.exe "%V"` |
| Ouvrir dans GitHub Desktop | FolderBackground | `"%LocalAppData%\GitHubDesktop\GitHubDesktop.exe" --cli-open="%V\."` (flag interne utilisé par le shim `github` — ajoute ET ouvre le dépôt, sans fenêtre console ; écrit en `REG_EXPAND_SZ` car install per-user vs clé HKCR machine-wide ; `\.` neutralise le backslash final des racines de lecteur ; proposé uniquement si GitHub Desktop ≥ 3.4.14 est installé) |

## Format JSON raccourcis rapides

```json
[
  { "page": 0, "row": 0, "col": 0, "name": "Mon app",    "type": "RunCommand",      "command": "explorer.exe \"C:\\dev\\projet\"", "iconPath": "", "iconProfilePath": "icons\\abc123.png" },
  { "page": 0, "row": 0, "col": 1, "name": "C:\\dev",    "type": "OpenFolder",      "command": "C:\\dev",                          "iconPath": "C:\\Windows\\explorer.exe" },
  { "page": 0, "row": 0, "col": 2, "name": "GitHub",     "type": "OpenUrl",         "command": "https://github.com",               "iconPath": "" },
  { "page": 0, "row": 0, "col": 3, "name": "Terminal",   "type": "OpenTerminal",    "command": "C:\\dev",                          "iconPath": "",
    "terminal": { "exePath": "wt.exe", "startingDirectory": "C:\\dev", "runCommand": "", "newTab": true, "extraArgs": "" } },
  { "page": 0, "row": 0, "col": 4, "name": "VS 2022",    "type": "SwitchToProcess", "command": "devenv.exe",                       "iconPath": "", "iconProfilePath": "icons\\def456.png",
    "processSwitch": { "processName": "devenv.exe", "executable": "C:\\...\\devenv.exe", "parameters": "C:\\dev\\Shope.sln" } }
]
```

- `type` optionnel — défaut `RunCommand` (rétrocompatible)
- `terminal` présent uniquement pour `OpenTerminal`
- `processSwitch` présent uniquement pour `SwitchToProcess`
- `iconProfilePath` chemin relatif au profil (`%APPDATA%\DockPad\`), prioritaire sur `iconPath`
- Colonnes : 0–5 | Lignes : 0–3 | `page` commence à 0

## Format JSON navigateurs

```json
{
  "browsers": [
    { "id": "a1b2c3", "name": "Chrome", "exePath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      "arguments": "", "iconProfilePath": "icons\\abc.png", "hidden": false, "order": 0 },
    { "id": "d4e5f6", "name": "Chrome Canary", "exePath": "…\\chrome.exe", "arguments": "", "order": 1 },
    { "id": "g7h8i9", "name": "Edge", "exePath": "…\\msedge.exe", "arguments": "", "order": 2 }
  ],
  "rules": [
    { "host": "github.com", "browserId": "a1b2c3" }
  ],
  "autoOpenSeconds": 5
}
```

- `id` : identifiant stable (8 hex aléatoires) référencé par les règles
- `host` d'une règle : peut inclure un port (`localhost:44351`) — sans port, seul le port par défaut du scheme matche
- `autoOpenSeconds` : délai avant ouverture automatique avec le navigateur n°1 (0 = désactivé ; défaut et absent = 2 s)
- `iconPath` peut porter un index d'icône au format registre (ex : `"C:\\...\\chrome.exe,4"`)
- `arguments` : si elles contiennent `%1`, il est substitué par l'URL, sinon l'URL est ajoutée en fin
- `iconProfilePath` chemin relatif au profil (`%APPDATA%\DockPad\`), prioritaire sur `iconPath`
- `hidden` : masqué = absent de la popup mais conservé dans la config
- Stocké dans `%APPDATA%\DockPad\browsers.json`, inclus dans la sauvegarde de configuration

## Styles des menus contextuels (App.xaml)

- `ContextMenu` : fond blanc, `CornerRadius=6`, `DropShadowEffect`, padding `0,4`
- `MenuItem` : template custom avec icône (col 16px), header, flèche `►` si sous-menu (`HasItems=True`), `Popup` pour les sous-menus
- `Separator` : ligne `#E0E0E0`, hauteur 1px, margin `8,4`
- **Important** : le template `MenuItem` doit contenir `<Popup x:Name="PART_Popup">` avec `<ItemsPresenter/>` pour que les sous-menus fonctionnent

## Icône de l'application

Fichier : `app.ico` (multi-taille : 16/32/48/256px)

Source : **Microsoft Fluent UI System Icons** — `ic_fluent_apps_list_32_color.svg`

Configuré dans :
- `.csproj` → `<ApplicationIcon>app.ico</ApplicationIcon>` (icône du `.exe`)
- `QuickAccessWindow.xaml` → `Icon="pack://application:,,,/app.ico"`
- `ContextMenuManagerWindow.xaml` → `Icon="pack://application:,,,/app.ico"`

> Toujours utiliser le pack URI (`pack://application:,,,/app.ico`) pour référencer `app.ico` dans les XAML situés dans des sous-dossiers, sinon WPF résout le chemin relatif depuis le sous-dossier (ex: `views/app.ico`) et lève une `IOException`.

> Tout `Border` avec un `CornerRadius` non nul doit avoir `SnapsToDevicePixels="True"` et `UseLayoutRounding="True"` pour éviter le rendu flou des bords arrondis (sub-pixel rendering WPF).

> Le template `MenuItem` dans `App.xaml` doit inclure `<Popup x:Name="PART_Popup">` avec `<ItemsPresenter/>` pour que les sous-menus (ex : "Déplacer vers la page") s'affichent correctement.

Icônes des tuiles (PNG 64×64) stockées dans `C:\dev\Dock-icons\`, sources :
- **Fluent UI System Icons** (Microsoft) — dossier, task-board
- **PKief/vscode-material-icon-theme** — claude, azure, pipeline
- **devicons/devicon** — visual-studio, vscode, fusion360
- **BambuLab/BambuStudio** (GitHub officiel) — bambu-studio
- **Simple Icons** — gmail, azure-devops
- **GitHub Octicons** — pull-requests

## Versioning & release

Version semver définie dans `DockPad.csproj` : `<Version>x.y.z</Version>`

Processus de release :
- **Jamais de bump de version dans une branche feature** — plusieurs features en cours = conflits sur `.csproj`/`CHANGELOG.md` et numéros faux (l'ordre de merge décide du bon numéro). Le bump se fait uniquement sur `master` (ou plus tard sur une éventuelle branche `preview` en amont de `master`)
- Les features sont développées en branche, validées, puis mergées dans `master`
- Une fois la ou les features mergées : bump du `<Version>` + entrée `CHANGELOG.md` en un commit `chore: bump version x.y.z` sur `master`, puis déploiement (voir Build : publish zip + extraction dans `C:\DockPad\`)
- Le garant du numéro de version est celui qui fait la release (merge + publish), jamais l'auteur de la feature — le bump reflète l'ensemble de ce qui part dans la release (`feat` → minor, `fix` seul → patch)

## Build

```bash
dotnet build

# Tests (DockPad.Tests, xUnit)
dotnet test DockPad.Tests

# Publish via le profil (méthode préférée)
dotnet publish -p:PublishProfile=FolderProfile
```

Build Debug : `bin\Debug\net8.0-windows\`

Le publish via `FolderProfile` :
1. Compile en Release framework-dependent (requiert .NET 8 sur la machine cible)
2. Copie `CHANGELOG.md` dans le dossier publish
3. Crée `release\DockPad-{version}.zip` et `release\DockPad-{version}-Changelog.md`
4. Supprime le dossier `publish\` intermédiaire

Après publish, vider `C:\DockPad\` et extraire le zip dedans.
