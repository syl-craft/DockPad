# DockPad

Application WPF C# (.NET 8, x64) pour gérer le menu clic droit Windows via le registre.
L'app démarre sans droits admin — l'élévation est demandée à la demande via un bouton UAC.

## Stack

- **WPF / .NET 8**  `net8.0-windows`, `UseWPF=true`, `UseWindowsForms=true` (pour NotifyIcon)
- **Registre**  lecture/écriture via `Microsoft.Win32.Registry`
- **Icônes**  `System.Drawing.Common` (NuGet) pour extraire les icônes `.exe`/`.dll`
- **JSON**  `System.Text.Json` (built-in) pour la config des raccourcis rapides
- **i18n**  RESX + assemblys satellites, et `SmartFormat` (NuGet, cœur seul — pas le bundle `SmartFormat.NET`, qui tire `Newtonsoft.Json`) pour le pluriel CLDR et les listes localisées
- **SQLite**  `Microsoft.Data.Sqlite` (NuGet, version alignée sur net8.0) — lecture seule de la base de Copilot CLI, seule source de consommation qui ne soit pas un fichier texte
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
    StringToBrushConverter.cs            Couleur en chaîne (« #34A853 ») → Brush

Models/
    ActionResult.cs                       Résultat d'une action des services (UI/MCP) : Ok + Data, ou échec + Error
    BrowserEntry.cs                       Modèle navigateur ou profil (id, name, exePath, arguments, icône, hidden, order, parentId, profileDirectory)
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
    AiUsage.cs                            Instantané de consommation d'un fournisseur IA (jetons, quotas, coût)
    UsageWindow.cs                        Une fenêtre de quota : consommé + heure de remise à zéro
    AiProviderEntry.cs                    Un fournisseur dans usage.json (nom, masquage, ordre, détection)
    UsageConfig.cs                        Contenu de usage.json (bandeau + liste des fournisseurs)
    UsageDisplayItems.cs                  Onglet, métrique et jauge du bandeau (modèles d'affichage)
    TerminalConfig.cs                    Config d'un terminal (exePath, startingDirectory, runCommand…)
    TerminalInfo.cs                      Informations d'un terminal détecté

Services/Localization/
    Loc.cs                                Seule porte d'accès aux chaînes traduites (C# pur, sans WPF)
    LocExtension.cs                       Extension de balisage {loc:T Clé} + ButtonFlash (feedback sans casser la liaison)

Resources/
    Strings.resx                          Anglais, langue neutre
    Strings.fr.resx                       Français, satellite fr\DockPad.resources.dll

Services/
    AppInfo.cs                            Infos application (VersionText affiché dans les footers)
    AppPaths.cs                           Racine du profil (%APPDATA%\DockPad ou DOCKPAD_PROFILE_DIR) — utilisée par toutes les configs
    BrowserActionService.cs               Actions navigateurs & règles de domaine, partagées UI ↔ MCP
    BrowserConfigService.cs              Load/Save browsers.json (%APPDATA%\DockPad\browsers.json)
    BrowserDetectionService.cs           Détection des navigateurs installés (Software\Clients\StartMenuInternet, HKLM+HKCU)
    BrowserProfileService.cs             Détection des profils Chromium (User Data\Local State) + fusion dans browsers.json
    BrowserRowLayout.cs                  Ordre d'affichage navigateurs + profils (groupes, en-têtes, ↑/↓, libellé « Chrome › Boulot »)
    BrowserRegistrationService.cs        Enregistrement per-user (HKCU) comme navigateur + lecture de l'état (non enregistré/enregistré/par défaut)
    ConfigLock.cs                         Verrou global des load-modify-save de configs (UI et MCP sérialisés)
    HotkeyService.cs                     P/Invoke RegisterHotKey / UnregisterHotKey (user32.dll)
    IconStoreService.cs                  Store des icônes du profil (%APPDATA%\DockPad\icons\) — SHA1 dédup, extraction .exe/.dll → .png
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
    TileLockState.cs                      Verrou du déplacement des tuiles (état + glyphe + infobulle, sans WPF)
    ShortcutActionService.cs              Actions sur la grille de raccourcis, partagées UI ↔ MCP (cœurs purs + enveloppes verrou/IO)
    ShortcutService.cs                   Load/Save shortcuts.json (%APPDATA%\DockPad\shortcuts.json)
    TerminalDetectionService.cs          Détection des terminaux installés + construction des arguments
    UrlPipeService.cs                    Named pipe DockPad_UrlPipe — serveur (instance principale) / client (instance relais)
    UrlRouterService.cs                  Règles de domaine, file d'URLs, orchestration popup/lancement
    UsageConfigService.cs                 Load/Save usage.json (%APPDATA%\DockPad\usage.json)

Services/Usage/
    IUsageProvider.cs                     Contrat d'un fournisseur : Probe() (détection) + ReadAsync() (lecture)
    AiProbe.cs                            Résultat d'une détection (disponible, chemin, démo, masqué par défaut)
    UsageProviderRegistry.cs              Seul point d'enregistrement des fournisseurs
    ClaudeUsageProvider.cs                Fournisseur Claude Code (dossier de départ injectable)
    ClaudeUsageReader.cs                  Scan des JSONL, déduplication, agrégation session/jour/mois
    ClaudeLimitsClient.cs                 Quota officiel via oauth/usage + lecture du jeton
    ClaudePricing.cs                      Tarifs par modèle → coût estimé en USD
    CodexUsageProvider.cs                 Fournisseur Codex (rollouts locaux, sans quota ni coût)
    CodexUsageReader.cs                   Scan des rollout-*.jsonl, événements token_count
    GeminiUsageProvider.cs                Fournisseur Gemini CLI (sessions locales, sans quota ni coût)
    GeminiUsageReader.cs                  Scan des sessions sous chats/, compteurs input/cached/thoughts/tool
    CopilotUsageProvider.cs               Fournisseur Copilot CLI (base SQLite, sans quota ni coût)
    CopilotUsageReader.cs                 Lecture de assistant_usage_events (Microsoft.Data.Sqlite)
    UsageAggregator.cs                    Agrégation session/jour/mois commune à tous les fournisseurs
    UsageWindows.cs                       Borne basse de scan (début du mois, ou bloc de session)
    DemoUsageProvider.cs                  Jeu de valeurs fixes paramétrable (captures, second onglet)
    AiDetectionService.cs                 Sonde les fournisseurs + fusion dans usage.json
    UsageService.cs                       Interroge les fournisseurs visibles en parallèle
    UsageFormat.cs                        Couleur de jauge, jetons compacts, heure de reset (fonctions pures)
    UsageViewModel.cs                     État affichable du bandeau (onglets, jauges, métriques)

Mcp/
    DockPadTools.cs                        Les 13 outils dockpad_* exposés au SDK MCP (relais vers le pipe)
    McpRelay.cs                            Hôte MCP stdio du mode --mcp (SDK ModelContextProtocol, aucune UI/mutex)

Views/
    ContextMenuManagerWindow.xaml/.cs    Gestion des entrées de menu contextuel Windows
    QuickAccessWindow.xaml/.cs           Grille de tuiles multi-pages (hotkey global)
    UsagePanel.xaml/.cs                  Bandeau Usage IA (aucun calcul, tout vient du ViewModel)

Dialogs/
    AppDialog.xaml/.cs                   Dialog custom styled (remplace MessageBox) — Confirm/Error/Warning/Info
    BrowserConfigDialog.xaml/.cs         Configuration navigateurs (détection/édition/règles/enregistrement)
    BrowserPickerWindow.xaml/.cs         Popup de choix du navigateur au clic sur une URL
    EntryDialog.xaml/.cs                 Ajout/modification d'une entrée de menu contextuel (registre)
    McpConfigDialog.xaml/.cs               Fenêtre « Serveur MCP » : options (activé/suppression) + journal de session
    PresetsDialog.xaml/.cs               Raccourcis prédéfinis
    SettingsDialog.xaml/.cs              Configuration du raccourci clavier global + démarrage auto + version
    ShortcutDialog.xaml/.cs              Ajout/modification d'une tuile d'accès rapide
    UsageConfigDialog.xaml/.cs           Fenêtre « Usage IA » : réglages du bandeau + fournisseurs détectés

DockPad.Tests/                           Projet xUnit (327 tests) : ActionResult/McpConfig/services d'actions/McpLogService/McpDispatcher/AppPaths
                                         + profils de navigateurs (détection, fusion, mise en page, arguments de lancement)
                                         + Usage IA (formatage, tarifs, quota, fusion, viewmodel)
                                         + lecteurs Claude, Codex, Gemini et Copilot (dossiers temporaires, base SQLite de fixture)

tools/
    get-startmenu-apps.ps1               Script PowerShell : résout les AppID Start Menu en chemins .exe
    inject-startmenu-shortcuts.ps1       Script PowerShell : injecte des raccourcis SwitchToProcess dans shortcuts.json
    McpShot/                             Outil console : capture les onglets de McpConfigDialog en PNG (doc)
    BrowserShot/                         Outil console : capture la popup de choix et la fenêtre Navigateurs en PNG
    UsageShot/                           Outil console : capture le bandeau Usage IA et sa fenêtre de réglages en PNG (doc)
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
- Toolbar : **☰ Menu** (déroulant) | **🔒 / ✓** (verrou du déplacement des tuiles) | **─** (réduire) | **⬇** (masquer dans la barre système)
- **Menu ☰** organisé en sections :
  - *Menu contextuel* : ☰ Gestion, 📋 Raccourcis prédéfinis
  - *Paramètres* : ⚙ Options, 🌐 Navigateurs, 🔌 Serveur MCP, 📊 Usage IA
  - *Configuration* : ↺ Actualiser, ✎ Modifier, 💾 Sauvegarder, 📁 Voir le dossier
  - ✕ Quitter l'application
- **Raccourci clavier actif** affiché en bas à droite (badge `Consolas`, mis à jour après changement dans Options)
- **Sauvegarder la configuration** : copie `shortcuts.json`, `pages.json`, `browsers.json`, `mcp.json` et `usage.json` dans `%APPDATA%\DockPad\.backup\` avec horodatage
- Config stockée dans `%APPDATA%\DockPad\shortcuts.json`
- Config pages stockée dans `%APPDATA%\DockPad\pages.json`
- **Clic droit sur une tuile** : 🖼 Changer l'icône | ✏ Modifier | ⧉ Dupliquer | ↗ Déplacer vers la page | 🗑 Supprimer
- **Clic droit sur une tuile OpenFolder** : section supplémentaire avec les entrées `Directory\Background\shell` du registre (substitution `%V` → chemin du dossier)
- **Clic droit sur une case vide** : ➕ Ajouter
- **Clic droit sur un bouton de page** : 🖼 Changer l'icône | ← / → Déplacer | 🗑 Supprimer la page
- **Drag & drop** entre tuiles pour les réorganiser — **verrouillé par défaut** (voir « Verrou du déplacement » ci-dessous)
- **Drag & drop depuis l'Explorateur** : glisser un dossier → raccourci `OpenFolder` (icône dossier par défaut `Assets/folder.png`) ; glisser un `.url` → raccourci `OpenUrl` (icône navigateur par défaut détectée via registre)
- **Déplacer vers la page** : place à la même position si libre, sinon première case disponible ; grisé seulement si la page est pleine

### Verrou du déplacement des tuiles
- Bouton de toolbar à gauche de **─** : **🔒** (verrouillé, style secondaire) ↔ **✓** (déverrouillé, fond bleu accent)
- Le clic sur une tuile lance son action, et le même geste manqué de quelques pixels la déplaçait : la réorganisation devient **un mode qu'on demande**, plutôt qu'un accident possible à chaque clic
- **Le verrou ne ferme qu'une porte** : `TileDrag_MouseMove` sort avant `DoDragDrop`. Le clic simple, les dépôts depuis l'Explorateur (`TileDrop_Drop` avec `_dragSource == null`), « ↗ Déplacer vers la page » du clic droit et la réorganisation des pages restent ouverts — ce sont des gestes délibérés, qu'on ne déclenche pas en visant une tuile
- **Ranger la fenêtre repose le verrou** (masquée ou réduite) : on ne peut pas oublier de le refermer. Branché sur `SyncWindowActivity()`, le point unique qui répond à « la fenêtre est-elle sous les yeux de l'utilisateur ? » — le même qui démarre et arrête le bandeau Usage. Un rappel posé dans chaque `Hide()` se serait perdu au premier appel ajouté ensuite
- **Rien n'est écrit sur le disque** : un état déverrouillé qui survivrait à un redémarrage annulerait la protection sans que personne s'en souvienne
- L'état vit dans `Services/TileLockState.cs` et non dans le code-behind, pour la même raison que `UsageViewModel` : le glyphe et l'infobulle sont des décisions, elles se testent sans WPF. Le code-behind ne fait que brancher le clic, lire les trois propriétés et choisir le style
- **`MinWidth="40"` sur le bouton** : sans elle, `✓` est plus étroit que `🔒` et toute la toolbar — barre de recherche comprise — se décalait de 4 px à chaque bascule
- Les tuiles, elles, ne changent pas d'apparence : le bouton porte l'information

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

### Store d'icônes (IconStoreService)
- `%APPDATA%\DockPad\icons\` est le **store** des icônes : la copie de référence utilisée pour l'affichage — pas un cache, rien n'expire
- À la sauvegarde, l'icône source est copiée dans le store (déduplication SHA1) ; les `.exe`/`.dll` sont extraits et sauvegardés en `.png`
- `IconProfilePath` (chemin relatif au profil, pointe dans le store) est la source d'affichage ; `IconPath` (chemin absolu d'origine) n'est gardé qu'à titre de provenance
- À la création/modification : si aucune icône spécifiée, l'icône de l'exe associé est utilisée automatiquement (RunCommand, SwitchToProcess, OpenTerminal)
- **↻ Actualiser** : resynchronise le store pour toutes les entrées existantes

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
- **Langue** en tête de fenêtre : `Automatique (Windows)` / `Français` / `English`. Stockée dans `HKCU\Software\DockPad\Settings\Language`, `""` = automatique — même convention que `TriggerFirst`/`TriggerSecond`. Application immédiate, sans redémarrage : la fenêtre se retraduit sous les yeux
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
- **Lancement** (`UrlRouterService.Launch`, ligne de commande construite par `BuildArguments`) : `Process.Start` avec l'URL entre guillemets en fin d'arguments ; si `Arguments` contient `%1`, il est substitué par l'URL à la place ; si l'entrée est un profil, `--profile-directory="<dossier>"` est placé **avant** les arguments de l'utilisateur
- **Profils de navigateur** : une entrée de `browsers.json` peut être un profil, rattaché à son navigateur par `parentId` + `profileDirectory` (`BrowserEntry`)
  - **Détection** (`BrowserProfileService`) : uniquement sur **↻ Redétecter**, jamais en tâche de fond — dossier *User Data* déduit du chemin de l'exe (l'exe doit être dans un dossier `Application`, le dossier au-dessus donne la variante : `Google\Chrome`, `Google\Chrome SxS`, `Microsoft\Edge Dev`, `BraveSoftware\Brave-Browser`, `Vivaldi`… sous `%LOCALAPPDATA%`), puis lecture de `profile.info_cache` dans `Local State` (ouvert en partage, le navigateur le garde ouvert) ; ordre : `Default` puis numéro croissant ; icône = `<User Data>\<profil>\Google|Edge Profile Picture.png` s'il existe, sinon celle du navigateur, copiée dans le store d'icônes
  - **Un seul profil détecté → aucune sous-entrée** : le navigateur nu suffit (comportement par défaut = dernier profil utilisé)
  - **Fusion** additive, clé `(parentId, profileDirectory)` : id, masquage et ordre préservés ; le nom suit le navigateur tant qu'il n'a pas été personnalisé dans DockPad (`detectedName` = dernier nom détecté), un profil disparu du navigateur **n'est pas** supprimé (ça détruirait ses règles de domaine)
  - **Affichage** (`BrowserRowLayout`, partagé popup ↔ configuration) : chaque navigateur suivi de ses profils indentés sous un filet vertical ; le navigateur reste choisissable (dernier profil utilisé) et sert de titre de groupe (SemiBold) ; masquer un navigateur en gardant ses profils le transforme en **en-tête non choisissable** (`ListBoxItem` `IsEnabled=False` → sauté par les flèches) ; badges clavier 1-9 attribués aux seules lignes choisissables ; ↑/↓ déplace un navigateur avec tout son groupe, un profil au sein de sa fratrie ; supprimer un navigateur supprime ses profils et leurs règles
  - Une règle de domaine peut viser un profil (même mécanique, `browserId` = id du profil) ; les ComboBox de l'onglet Règles libellent les profils `Chrome › Boulot`
  - Le chemin de l'exécutable d'un profil n'est pas modifiable : il suit celui de son navigateur
  - Firefox (`-P "nom"` via `profiles.ini`) n'est **pas** géré — seuls les navigateurs Chromium
- **Configuration (`BrowserConfigDialog`)** : ☰ → Paramètres → 🌐 Navigateurs — fenêtre 680×760 redimensionnable, **2 onglets** (`TabControl` plat, soulignement bleu sur l'onglet actif)
  - **Onglet Navigateurs** : section enregistrement (état + 2 boutons + champ **« Ouverture automatique : N secondes »**, clamp 0-300, sauvegarde immédiate) ; auto-détection (`BrowserDetectionService`, parcours `Software\Clients\StartMenuInternet` HKLM puis HKCU, DockPad exclu, doublons ignorés, **icône lue depuis la valeur `DefaultIcon` avec son index** — ex. Chrome Canary = `chrome.exe,4` pour l'icône jaune) au premier chargement ou si la liste est vide (fichier corrompu) et via **↻ Redétecter** ; **case à cocher de visibilité par ligne** (décochée = absent de la popup, badge « masqué », conservé) ; édition (nom, chemin exe, arguments — ex. `--profile-directory="Profile 1"`, `--incognito`, `-inprivate`), monter/descendre, supprimer, **+ Ajouter**
  - **Onglet Règles de domaine** : recherche live sur le host + filtre par navigateur (combinables), ComboBox par ligne pour réassocier le navigateur (sauvegarde immédiate), suppression par ligne, compteur « N / M règle(s) », état vide explicite ; création uniquement depuis la popup ; supprimer un navigateur supprime ses règles associées
  - Rechargement croisé : si le picker sauvegarde une règle pendant que le dialog est ouvert, `Activated` + comparaison `File.GetLastWriteTimeUtc` rechargent le snapshot
  - **Attention** : les boutons carrés (34px) doivent avoir `Padding="0"` — le `Padding 16,8` hérité du style `PrimaryButton` ne laisse que 2px au glyphe (boutons invisibles)
- Icônes chargées via `LoadIcon` (même pattern dans `BrowserPickerWindow` et `BrowserConfigDialog`) : extraction `.exe`/`.dll` via `System.Drawing.Icon.ExtractAssociatedIcon` puis `DeleteObject` sur le handle GDI (anti-fuite mémoire) ; `IconStoreService.ParseIconRef` découpe `chemin[,index]` et `Icon.ExtractIcon` respecte l'index (négatif = ID de ressource)
- Config stockée dans `%APPDATA%\DockPad\browsers.json`, incluse dans **💾 Sauvegarder la configuration**

### Internationalisation (français / anglais)

Les chaînes vivent dans deux RESX : `Resources/Strings.resx` en **anglais neutre** et
`Resources/Strings.fr.resx` en satellite. L'anglais est le neutre parce que le repli de
`ResourceManager` remonte à la langue neutre — c'est ce que verra un poste japonais, il doit donc
être une vraie langue et pas un dépotoir de clés.

- **Une seule porte** : `Services/Localization/Loc.cs`, C# pur, **aucune référence WPF**. C'est ce
  qui permet de traduire `UsageViewModel`, les fournisseurs de consommation et les services d'action
  tout en les gardant testables sans instance `Application`. `Loc.T("Clé")` pour un texte,
  `Loc.F("Clé", args)` pour un gabarit
- **Convention de clés** `Zone_Element` en `PascalCase`. Jamais de clé dérivée du texte anglais : le
  jour où le texte change, la clé mentirait
- **La bascule à chaud tient à une ligne** : `SetCulture` notifie `"Item[]"`, ce qui invalide toutes
  les liaisons d'indexeur de l'application d'un coup. `{loc:T Clé}` ne fabrique rien d'autre que
  cette liaison — aucun abonnement à gérer par fenêtre, et une fenêtre ajoutée plus tard en
  bénéficie sans rien brancher
- **Quatre affectations de culture, pas deux** : `CurrentUICulture` et `CurrentCulture` pour le
  thread courant, `DefaultThreadCurrentCulture`/`DefaultThreadCurrentUICulture` pour ceux qui
  n'existent pas encore — sans elles les `Task.Run` des fournisseurs formatent dans la culture
  d'origine sous une interface déjà traduite
- **« Automatique » lit la langue capturée au chargement du type**, pas `CurrentUICulture` :
  `SetCulture` écrit dedans, donc la lire aurait figé « automatique » sur la dernière langue choisie
- **WPF ignore `CurrentCulture` pour les `StringFormat` de liaison** : il lit
  `FrameworkElement.Language`. `App.ApplyWpfLanguage` la pose au `Loaded` de **chaque** fenêtre via
  un gestionnaire de classe, et non par un `OverrideMetadata` — celui-ci ne s'appelle qu'une fois et
  figerait la langue du démarrage, si bien qu'une fenêtre ouverte après une bascule hériterait de
  l'ancienne
- **Un `StringFormat` ne peut pas porter un texte traduit** : les deux qui en portaient
  (`Remise à zéro à {0}`, `Ouvrir {0}`) sont passés par le ViewModel (`ResetTooltip`,
  `UsageUrlTooltip`), conformément à « aucun calcul dans le XAML »
- **Assigner `Content` casse la liaison `{loc:T}`** : c'est une valeur locale, qui la remplace
  définitivement. Les feedbacks « Copié ✓ » passent par `ButtonFlash`, qui mémorise la liaison et la
  repose au lieu de recopier une chaîne — sinon le bouton devient sourd aux changements de langue
  pour le reste de la session. `ButtonFlash` tient un registre à clés faibles des feedbacks en
  cours : sans lui, un **second clic** pendant l'animation lisait une liaison déjà effacée par le
  premier et mémorisait « Copié ✓ » comme texte d'origine, ce qui bloquait le bouton sur le message
  pour de bon. Corollaire : ne posez pas de `{loc:T}` sur une propriété que le code affecte aussi —
  le verrou des tuiles n'en a pas sur son `ToolTip`, `ApplyTileLock` l'écraserait dès l'init
- **Ce qui est construit en code ne se retraduit pas seul.** `QuickAccessWindow` et `UsageViewModel`
  s'abonnent donc à `LanguageChanged` : badge de raccourci, infobulles de tuiles, libellés de type,
  menus contextuels, libellés et nombres du bandeau. Le piège : la langue s'applique **au changement
  de liste**, pas au bouton Sauvegarder — annuler les Options après avoir changé de langue laissait
  sinon la grille dans l'ancienne, le chemin d'annulation ne rafraîchissant rien
- **Trois chaînes sont rendues par le fournisseur** et voyagent dans l'instantané : notice de quota,
  sa précision technique, note de coût. `UsageViewModel.OnLanguageChanged` fait donc un `Rebuild`
  (libellés immédiats) **puis** une relecture — sinon elles restaient dans l'ancienne langue jusqu'au
  tic suivant, au milieu de libellés déjà basculés
- **Les libellés construits en code ne se retraduisent pas seuls** : les listes de `SettingsDialog`
  (modificateurs, touches) et ses avertissements sont refaits sur `Loc.LanguageChanged`, sélection
  conservée

#### Pluriel et listes (SmartFormat)
RESX n'a aucun moteur de pluriel et le BCL .NET n'expose pas les catégories CLDR. `SmartFormat`
les apporte, et ses règles sont exactes pour les deux langues — vérifié dans son source :

```csharp
{ "en", DualOneOther },        // n == 1        → « 0 rules »
{ "fr", DualFromZeroToTwo },   // 0 <= n < 2    → « 0 règle »
```

Les deux langues n'ont que deux formes mais **ne basculent pas au même endroit** : les deux
raccourcis qu'on écrit d'instinct sont faux tous les deux (`n > 1` donne « 0 rule », `n == 1` donne
« 0 règles »). La règle appartient à la langue, jamais au site d'appel. Gabarit dans la valeur :
`{0} {0:plural:rule|rules}`.

Son `ListFormatter` porte la conjonction localisée : `{1:list:{}|, | and }` remplace le
`string.Join(" et ", …)` qui devenait faux en anglais.

#### Ce qui n'est pas traduit, et pourquoi
- **Le journal** (`LogService.*`) : un log qui change de langue selon le poste n'est plus grep-able,
  et son lecteur est le développeur
- **Les causes d'indisponibilité du quota** — « HTTP 429 TooManyRequests », « access token missing or
  expired », « unknown response shape (N bytes) » : diagnostics, donc **en anglais et jamais
  traduits**, comme les noms de type d'exception qui les côtoient. Elles partent au journal, où une
  langue stable est ce qui rend une trace comparable d'un poste à l'autre, et s'affichent en
  infobulle derrière une phrase, elle, traduite
- **Les messages d'exception** (`throw new …`) : mêmes raisons, et l'utilisateur ne les voit que
  derrière une phrase déjà traduite qui porte le sens
- **Les messages MCP** : leur lecteur est un modèle, pas un humain. L'onglet Journal de la fenêtre
  MCP affiche le message **brut** du service — il rapporte ce qui a été renvoyé à Claude, il ne doit
  pas le réécrire
- **Les noms d'outils** (`Claude Code`, `Codex`…) et **les noms stockés** dans `usage.json` /
  `browsers.json` : un nom persisté et personnalisable ne peut pas suivre la langue sans écraser la
  personnalisation
- **Les noms de langue** dans le sélecteur : une langue s'écrit dans sa propre langue, c'est ce qui
  permet de la retrouver quand l'interface est dans une langue qu'on ne lit pas
- **Les noms de touches non nommées** (lettres, F1-F12, pavé numérique, flèches) : identiques
  partout, les mettre dans le magasin serait du bruit. Les dix nommées passent par
  `HotkeyService.Display`

#### Nombres, heures, registre
- `UsageFormat` n'épingle plus `fr-FR` : il lit `Loc.Current` — « 12,4k » / « 12.4k »
- **Les gabarits d'heure et les suffixes sont des clés** : le `h` de « 14h00 » et le « Md » des
  milliards sont des conventions françaises écrites en dur, qu'aucune `CultureInfo` ne corrige.
  L'anglais dit « 14:00 » et « B »
- `ClaudePricing.Format` **reste** en `InvariantCulture` : le montant est en dollars parce que la
  source facture en dollars
- **Prédéfinis** : la clé de registre est un identifiant ASCII stable, donc changer de langue ne crée
  aucun doublon. `PresetService.CompareStatus` compare **aussi le nom affiché** — sans ça une entrée
  installée dans l'autre langue s'annonçait « déjà installée » et le bouton refusait de la
  réappliquer, rendant la traduction du menu contextuel inatteignable. La mise à jour reste
  manuelle, par le bouton
- La description montrée par Windows dans « Applications par défaut » est écrite dans la langue du
  moment de l'enregistrement ; se réenregistrer la met à jour

#### Tests (sans WPF)
`Loc` (résolution, repli, threads d'arrière-plan, notification d'indexeur), pluriel aux trois valeurs
qui séparent les deux langues, **parité des clés**, valeurs non vides, **placeholders identiques**
entre langues, **parsabilité de tous les gabarits** — ce dernier ramène à la suite de tests le mode
de panne qu'on introduit en mettant de la syntaxe dans les valeurs.

Quatre gardes de cohérence, toutes vérifiées par mutation :

- **aucun texte littéral dans un XAML** (`XamlLiteralGuardTests`) ;
- **aucun texte français d'interface en dur dans le C#** (`FrenchLiteralGuardTests`). Le critère est
  la présence d'un **mot-outil français**, pas d'un accent : le balayage manuel de la migration
  cherchait des accents et a laissé passer « Tous les navigateurs », « La page est pleine »,
  « Nouveau navigateur » et « Chemin du dossier * » — quatre libellés qui n'en portent aucun, dont un
  trouvé par l'utilisateur après la revue. Sont exclus : les appels à `LogService`, les messages
  d'exception (diagnostics, et lus dans le journal) et les fichiers qui parlent au serveur MCP ;
- **toute clé citée existe** — `Loc.T` rend `[Clé]` au lieu de lever, donc une faute de frappe ne se
  verrait que sur l'écran concerné ;
- **aucune clé du magasin n'est orpheline** — la parité vérifie la symétrie des deux langues, pas
  leur utilité : deux doublons morts s'y étaient glissés avec la même valeur qu'une clé existante.

Le scanner de clés lit les **arguments** de chaque appel, pas seulement la forme
`Loc.T("Clé")` : les clés voyagent aussi dans un ternaire — `Loc.T(cond ? "A" : "B")` — et un
détecteur qui ne verrait que la forme directe déclarerait ces clés orphelines.

> **Un test de localisation doit poser la langue explicitement.** Sinon il hérite de celle laissée
> par une autre classe et passe ou casse selon l'ordonnancement. La parallélisation de xUnit est
> désactivée pour cette raison (et pour l'état statique du journal MCP).

### Bandeau Usage IA
Bandeau sous la grille (`Views/UsagePanel.xaml`), 4ᵉ ligne de `QuickAccessWindow` : toolbar / grille / **bandeau** / pagination.

**Le `Collapsed` porte sur le `UserControl`, pas sur son contenu.** La fenêtre hôte pose une largeur et une hauteur explicites sur ce contrôle pour l'aligner sur les tuiles : replier le seul `Border` intérieur laissait ces 90 px et leur marge occuper la place, soit un grand vide sous la grille quand le bandeau est désactivé. La visibilité est donc posée sur le contrôle lui-même, en réaction à `UsageViewModel.IsVisible`. La fenêtre étant en `SizeToContent`, elle se rétracte d'autant.

**Désactivé = au repos.** Aucun fournisseur n'est interrogé (`RefreshAsync` court-circuite `UsageService`), et le `DispatcherTimer` est arrêté : sans ça il continuait de battre chaque minute pour relire la config et constater qu'il n'y a rien à faire.

- **Contenu** : un onglet par fournisseur (pastille colorée + nom + badge « démo »), les deux jauges *session* et *semaine* sur une seule ligne chacune (pourcentage **consommé** en gras dans la couleur de la jauge, barre de 6 px, `↻ heure de reset`), puis six colonnes de métriques — Session, Jour, Mois, Requêtes, Coût est., Modèle. Coût décoché → cinq colonnes
- **Lien vers la page officielle du fournisseur** à droite de la ligne haute : sa pastille, cliquable. L'affordance repose sur le curseur main, le fond au survol et l'infobulle qui nomme l'URL — la pastille est identique à celle de gauche, rien d'autre ne la distingue. L'URL est portée par `AiUsage.UsageUrl`, décidée dans le code du fournisseur et jamais lue depuis un fichier ; le schéma est tout de même vérifié avant `Process.Start` — avec `UseShellExecute`, une chaîne quelconque ouvrirait aussi bien un fichier ou une commande. Vide → le lien est masqué (cas du provider Démo dans l'application)
- **Sablier pendant la lecture** : la pastille du fournisseur cède la place à un rond gris tournant, et les valeurs précédentes restent affichées — un rafraîchissement réel prend le temps de parcourir les transcripts (mesuré 1,7 s), et l'attente doit se voir. Le `RotateTransform` vit dans un `ControlTemplate` et non dans un `Setter` de `Style` : une valeur de `Setter` est une instance unique partagée, et l'animer lève une exception si elle est gelée. L'animation ne tourne que pendant l'attente (`EnterActions`/`ExitActions`)
- **Libellés courts (« session », « semaine ») et infobulle explicite** : le libellé seul ne dit pas si le chiffre est le consommé ou le restant, l'infobulle donne les deux. Le mot « utilisée » dans le libellé coûtait la largeur des barres
- **Un seul fournisseur visible → aucun onglet** : nom et pastille en libellé statique. Un onglet unique et cliquable suggère un choix qui n'existe pas. Le seuil est le nombre de fournisseurs visibles, pas une constante
- **La barre suit le restant, pas le consommé** : elle doit s'accorder avec le nombre affiché juste au-dessus. Une barre remplie à 62 % sous un « 38 % » se lit comme un défaut (vu à la capture). Jauge de carburant : elle se vide quand on consomme
- **Les onglets ont leur propre ligne** : au-delà de deux fournisseurs, onglets et jauges ne tiennent pas côte à côte à 850 px, et le texte de reset se tronquait
- **Aucun calcul dans le XAML** : tout vient de `UsageViewModel`, testé sans WPF. Les couleurs voyagent en chaînes (`#34A853`) converties par `StringToBrushConverter` — la décision de couleur appartient à la logique
- **Valeur inconnue → `—`, jamais `0`.** Une session à zéro signifie « aucun bloc actif », pas « rien consommé »
- **Quota inconnu → la jauge entière disparaît**, pas seulement sa barre. `UsedPct` vaudrait 0 et afficherait « 0 % session », soit une mesure affirmée là où il n'y a pas de donnée. Un vide ne prétend rien. C'est l'état courant au démarrage, avant que la première lecture ait abouti
- **Mais un vide ne dit pas non plus ce qu'il se passe.** Quand le quota est *refusé* (429, jeton expiré, forme de réponse inconnue), la place des jauges porte une notice `⚠ Quota indisponible — nouvelle tentative dans N min`, avec la cause technique en infobulle. Le texte vient du fournisseur (`AiUsage.QuotaNotice` / `QuotaNoticeNote`), comme la devise du coût : un quota **absent par nature** — Codex, Gemini, Copilot n'en exposent aucun — n'est pas une panne et ne dit rien. Sans cette notice, la seule trace de l'indisponibilité vivait dans le fichier de log, où personne ne va regarder
- **Géométrie calée sur les tuiles** : largeur = largeur du bloc de tuiles moins les marges horizontales d'une tuile (pour affleurer leurs bords visibles), hauteur = hauteur d'une tuile. Les deux sont lues **sur une tuile réelle** au premier `LayoutUpdated`, jamais recopiées depuis le style — une seule source de vérité, qui suit un changement de taille de tuile. `LayoutUpdated` et non `Loaded` : la mise en page a lieu même sans fenêtre affichée, ce dont l'outil de capture a besoin ; se désabonner après le premier passage évite la boucle
- **La fenêtre prend la hauteur de son contenu** (`SizeToContent="Height"`, toutes les lignes en `Auto`, `MaxHeight` clampé sur la zone de travail) : c'est le patron documenté pour un contenu statique, et la grille 4 × 6 à tuiles fixes en est un. Une hauteur fixe ne tient pas — à 630 px la ligne de la grille recevait 432 px pour 425 nécessaires, et une pagination un peu plus haute (boutons de page avec icône) suffisait à faire apparaître une barre de défilement. Sans mou dans les lignes, l'écart grille → bandeau est déterminé par les seules marges, sans avoir à ancrer la grille
- **Conséquence** : la fenêtre ne se redimensionne plus en hauteur (rien à y gagner, la grille a un nombre de rangées fixe). La largeur reste ajustable
- **Cycle de vie** branché sur `IsVisibleChanged` + `StateChanged` de la fenêtre, pas sur chaque `Show()`/`Hide()` : un seul point, qui couvre les appels ajoutés plus tard. Rafraîchissement toutes les 60 s **uniquement quand la fenêtre est visible et non réduite** — DockPad passe l'essentiel de son temps dans la barre système
- **Le raccourci global déclenche aussi la synchronisation**, explicitement : quand la fenêtre est déjà visible, ni `WindowState` ni `Show()` ne changent quoi que ce soit, donc aucun événement ne se déclenche — le bandeau restait tel quel, sans lecture ni sablier, alors que passer au premier plan est le moment où l'on veut des chiffres à jour
- **Une invocation dépassée de `RefreshAsync` ne publie rien** : ni la fin de l'attente, ni ses instantanés. Sans cette garde, la séquence masquer-réafficher éteignait le sablier pendant la lecture suivante, et le chemin d'exception de l'ancienne vidait le bandeau *après* que la récente l'avait rempli. La source annulée n'est pas libérée non plus : son jeton peut être détenu par une requête en vol, et la libérer levait une `ObjectDisposedException` attrapée plus haut comme une panne de lecture
- **Glyphes** : `FontFamily="Segoe UI Symbol, Segoe UI Emoji, Segoe UI"` obligatoire sur les pastilles — Segoe UI ne contient ni ✳ (U+2733) ni ⊕, qui tombent en tofu

#### Fournisseurs (`Services/Usage/`)
`IUsageProvider` porte la détection (`Probe()`) **et** la lecture (`ReadAsync()`) : un assistant = un fichier. `UsageProviderRegistry.All` est le seul point d'enregistrement — l'arrivée de Codex, Gemini et Copilot n'a touché ni le bandeau ni la fenêtre de réglages, ce qui valide la frontière. `UsageService` reçoit sa liste en paramètre de construction (registre par défaut), ce qui permet aux tests et à `tools/UsageShot` de substituer la leur.

| Fournisseur | Source | Quota | Coût |
|---|---|---|---|
| `ClaudeUsageProvider` | `~/.claude/projects/**/*.jsonl` | oui, via `oauth/usage` | oui, `ClaudePricing` |
| `CodexUsageProvider` | `~/.codex/{sessions,archived_sessions}/**/rollout-*.jsonl` | non | non |
| `GeminiUsageProvider` | `~/.gemini/tmp/<hash>/chats/session-*.json{,l}` | non | non |
| `CopilotUsageProvider` | `~/.copilot/session-store.db` (SQLite) | non | non |
| `DemoUsageProvider` | valeurs fixes paramétrables | oui | oui |

- **Seul Claude a un quota et un coût.** Les trois autres n'exposent pas de pourcentage de limite lisible localement, et aucun tarif public fiable ne leur est appliqué : leurs deux jauges restent masquées et la colonne de coût affiche un tiret. **Inventer un tarif serait pire qu'afficher un tiret** — un montant faux se lit comme un montant
- **L'identité visuelle est déclarée une seule fois par fournisseur** (une constante privée, lue par `Probe()` et par l'instantané) et voyage jusqu'à la fenêtre de réglages via `AiProbe`. Une seconde table de littéraux dans le dialogue aurait montré un rond gris au prochain assistant ajouté, alors que le bandeau lui affichait sa vraie couleur
- **La précision affichée au survol du coût vient du fournisseur** (`AiUsage.CostNote`), pour la même raison que la devise : lui seul sait comment sa source facture. « Un abonnement Max ou Pro ne facture pas au jeton » n'a aucun sens sur un onglet Codex ou Gemini
- **Le dossier de départ est injectable** sur les quatre providers réels (repli sur `%USERPROFILE%`) : c'est ce qui rend détection et scan testables sur un dossier temporaire, sans toucher au profil réel
- **`CODEX_HOME` et `COPILOT_HOME` sont respectées**, comme le font leurs CLI. Sans ça, un utilisateur qui a déplacé son dossier verrait un zéro silencieux
- **`ReadAsync` renvoie `null`** pour « rien à afficher » — c'est le cas normal, pas une erreur. Une exception est attrapée par `UsageService`, journalisée en `Warn`, et traitée comme `null` : les autres fournisseurs s'affichent
- **Détecté mais inactif = un onglet à zéro, pas une disparition.** `ReadAsync` ne renvoie `null` que si la sonde dit le fournisseur absent ; s'il est installé mais qu'aucune consommation ne tombe dans la fenêtre, il rend `UsageAggregator.Empty`. Sinon un assistant installé qu'on n'a pas utilisé ce mois-ci s'affiche « détecté » dans les réglages et reste introuvable dans le bandeau, sans rien qui explique l'écart — le cas vécu avec Gemini, dont la seule session du mois ne portait aucun jeton. **Disparaître du bandeau veut dire « pas installé », et rien d'autre**
- **Un fournisseur masqué n'est pas interrogé du tout** : lire pour ne pas afficher, c'est du disque et du réseau pour rien
- **La lecture passe par `Task.Run`** chez les quatre providers réels : elle parcourt des fichiers (mesuré 2,5 s sur le profil Claude réel) et gèlerait le thread d'interface avant le premier `await`

#### Agrégation commune (`UsageAggregator`)
Extraite de `ClaudeUsageReader` à l'arrivée des trois autres : chacun lit sa source à sa façon, tous comptent le temps de la même manière. `Aggregate` prend la tarification en paramètre (`null` = pas de coût), et `UsageWindows.ScanStart` donne la borne basse du scan — début du mois, sauf le premier du mois au petit matin où le bloc de session peut avoir démarré le mois précédent.

#### Lecture des journaux Claude (`ClaudeUsageReader`)
- **Déduplication sur `(message.id, requestId)` — indispensable** : reprise de session et sidechains réécrivent le même message ailleurs. Mesuré sur 407 fichiers réels : **49 % des lignes `assistant` sont des doublons**. Sans dédup, tous les totaux sont à peu près doublés
- **Fenêtre par `mtime`** : un fichier modifié avant le début de la période n'est pas ouvert
- **`ScanRoots(home)` est la seule fonction qui sait où chercher** — le scan, la détection et les tests l'appellent. Deux listes de littéraux séparées, et c'est l'une des deux qui pourrit sans qu'un test le voie
- **Timestamps UTC → `LocalDateTime`**, jamais `ToLocalTime().DateTime` : le second rend un `Kind` *Unspecified*, qui laisse passer un mélange UTC/local inaperçu (les bornes jour et mois sont locales)
- **Une entrée sans aucun jeton est écartée** : ce n'est pas un appel facturé. Claude Code en écrit pour ses messages générés localement, sous le modèle `<synthetic>` — mesuré 25 entrées à zéro jeton sur un mois. Les garder ne changeait pas les totaux, mais le modèle affiché est celui de l'entrée la plus récente : une synthétique en dernier faisait afficher `<synthetic>` à la place du vrai modèle
- **Bloc de session ancré** : il démarre à la première activité qu'aucun bloc ne couvre et dure 5 h. Un bloc fermé avant maintenant donne zéro, pas un total périmé
- Lecture en `FileShare.ReadWrite` et JSON tronqué toléré : Claude Code écrit pendant qu'on lit

#### Lecture de Codex (`CodexUsageReader`)
Lignes `type:"event_msg"` avec `payload.type:"token_count"` ; le delta du tour est dans `payload.info.last_token_usage`.

- **Les deux racines doivent être lues.** Codex déplace un rollout de `sessions` vers `archived_sessions` : ce n'est pas une autre consommation mais le même fichier qui bouge. N'en lire qu'une ferait « disparaître » du passé
- **Correspondance des compteurs** : `input_tokens` est le prompt entier, `cached_input_tokens` en est un sous-ensemble — soustrait pour ne pas compter deux fois. `output_tokens` inclut déjà le raisonnement, `reasoning_output_tokens` n'est donc pas ajouté
- **Filtre textuel avant l'analyse JSON** : l'essentiel d'un gros rollout est de la conversation, qui ne contient pas le marqueur `token_count`
- **Limite connue : une session dérivée peut être comptée deux fois.** Un fork rejoue l'historique du parent dans un nouveau rollout, qui réémet ses événements `token_count`. Contrairement à Claude, ces événements ne portent pas d'identifiant de message : il n'y a rien à dédupliquer entre fichiers. L'implémentation de référence résout le cas en comparant les rollouts entre eux, avec plusieurs centaines de lignes de machinerie — hors de proportion tant que personne n'a constaté l'écart
- **Le quota existe mais n'est pas lu** : il faudrait lancer `codex app-server --stdio` et dialoguer en JSON-RPC, soit un processus enfant chaque minute pour deux nombres

#### Lecture de Gemini (`GeminiUsageReader`)
Un document par session sous `chats/`, avec un tableau `messages` dont les réponses portent un objet `tokens` (`input`, `cached`, `output`, `thoughts`, `tool`, `total`). La variante `.jsonl` existe aussi, un objet par ligne.

- **Seul `chats/` est scanné.** Le voisin `logs/` contient des `.jsonl` de trace console et réseau, sans aucune consommation — mesuré 6 Mo sur une machine réelle. Les ouvrir coûterait le prix d'un gros fichier pour zéro entrée. L'implémentation de référence scanne tout `~/.gemini/tmp`
- **Correspondance des compteurs, qui préserve `total`** : `input` contient déjà `cached`, soustrait pour ne pas compter deux fois ; `thoughts` est du raisonnement compté à part par Gemini mais bien de la sortie, donc ajouté à `output` ; pas d'écriture de cache. Vérifié sur les fichiers réels : 12 128 + 74 + 991 = 13 193, le `total` de la source
- **Le même `id` peut être réécrit** (réponse mise à jour en cours de route) : la dernière valeur gagne, d'où un dictionnaire par fichier plutôt qu'une liste
- Horodatage du message, à défaut `startTime` de la session

#### Lecture de Copilot (`CopilotUsageReader`)
Table `assistant_usage_events` de `~/.copilot/session-store.db` — une ligne par appel facturé.

- **Seule source du projet qui soit une base de données**, d'où la dépendance `Microsoft.Data.Sqlite` et les binaires natifs SQLite qu'elle embarque dans la publication. Version **alignée sur le framework cible** (8.0.x) et non la dernière : le SDK installé est un .NET 10, l'application est publiée pour .NET 8
- Ouverture en **lecture seule** : Copilot garde sa base ouverte
- **`input_tokens` est le prompt entier** : lectures et écritures de cache en sont un sous-ensemble. Sans les soustraire, le même prompt serait compté trois fois
- **Le filtre SQL sur `created_at` est un pré-filtre grossier** : la colonne est du texte, donc la comparaison est lexicographique. On recule d'un jour entier pour qu'aucune ligne de la fenêtre ne passe à la trappe à cause d'un décalage horaire écrit dans la valeur, puis on filtre pour de vrai après analyse
- **La clé porte le chemin de la base** : l'identifiant de ligne n'est unique que dans une base, et `COPILOT_HOME` peut en désigner plusieurs
- Table absente, base verrouillée ou fichier qui n'est pas une base → rien d'affiché pour ce fournisseur, ce qui vaut mieux qu'un total faux

#### Quota officiel (`ClaudeLimitsClient`)
`GET https://api.anthropic.com/api/oauth/usage`, en-têtes `Authorization: Bearer <jeton>` + `anthropic-beta: oauth-2025-04-20`. Réponse acceptée sous deux formes : champs hérités `five_hour`/`seven_day`, ou liste `limits[]` (`kind` = `session` / `weekly_all`) — les hérités priment.

- **Jamais un chemin critique** : l'endpoint n'est pas documenté et cassera. Jeton absent, expiré, 401, 429, réseau coupé, forme inconnue → `null`, donc jauges masquées et jetons conservés. Un `LogService.Info` une seule fois par session
- **L'échec est nommé, pas seulement constaté** : `FetchAsync` renvoie le quota **et** une raison — code de statut HTTP, « forme de réponse inconnue », ou type d'exception. Jamais le jeton, jamais le corps de la réponse, jamais le message d'exception (qui peut embarquer l'URL). Un journal qui dit seulement « indisponible » ne permet pas de distinguer un 401 d'un 429, qui est la première question qu'on se pose
- **Une annulation ne consomme pas le créneau de cinq minutes.** Le créneau est posé avant l'appel pour ne pas laisser deux lectures partir de front, mais il est rendu si l'appel est annulé — ce que fait la séquence masquer/réafficher la fenêtre. Sinon une lecture qui n'a rien tenté coûtait cinq minutes de jauges vides, sans un mot à l'écran ni au journal : le cas le plus déroutant, parce qu'il ne laisse aucune trace
- **Le journal suit chaque *changement* de cause**, et non « une seule fois par session » : un 429 puis un changement de forme de réponse ne laissaient qu'une trace, celle du premier. Le retour à la normale est journalisé aussi. L'état est porté par le fournisseur (champ d'instance), plus par un `static` — le registre n'en construit qu'un, et les tests ne se contaminent plus entre eux
- **Cadence limitée à cinq minutes, dernière valeur conservée quinze.** L'« intermittence » observée était un **HTTP 429** provoqué par DockPad lui-même : le bandeau se rafraîchit chaque minute et appelait le quota à chaque fois, pour des fenêtres qui durent 5 h et 7 jours. Entre deux appels les jauges gardent la valeur précédente — un pourcentage de quelques minutes vaut mieux qu'un vide sur une fenêtre de plusieurs heures — et au-delà de quinze minutes elles se masquent, parce qu'un chiffre périmé affirme quelque chose de faux. C'est aussi ce qui évite de marteler l'endpoint après un refus
- **Traitement du secret** : le jeton vient de `%USERPROFILE%\.claude\.credentials.json` (`claudeAiOauth.accessToken`) — Windows n'a pas de keychain, ce fichier est la seule source. Il reste local à la méthode qui l'utilise : **jamais journalisé, jamais recopié dans une config, jamais envoyé ailleurs que sur `api.anthropic.com`**
- **Deux pièges couverts par des tests** : `claudeAiOauth` explicitement `null` se décode en élément *présent* — tester la seule présence de la clé lirait un état déconnecté comme valide ; et `utilization` est un **pourcentage**, pas une fraction — l'heuristique « ≤ 1 donc fraction » transformerait une utilisation réelle de 1 % en 100 %, soit une fausse alerte rouge

#### Coût (`ClaudePricing`)
Tarifs publics par million de jetons, correspondance **par préfixe** de nom de modèle (les transcripts portent des identifiants datés). Multiplicateurs de cache issus de la documentation Anthropic : écriture ×1,25 (TTL 5 min), lecture ×0,1.

- **Modèle inconnu → repli sur le tarif Sonnet, pas zéro** : un coût nul se lit comme « gratuit ». Les modèles retirés sont donc sous-estimés, assumé — la colonne annonce « Coût est. »
- **Devise : celle de la source, aucune conversion.** Convertir demanderait un taux figé qui dérive, ou un appel réseau, pour une valeur déjà approximative. Les tarifs Claude étant en USD, la colonne affiche `$3.80` là où la maquette montrait « 3,80 € ». `AiUsage.Cost` est une **chaîne déjà formatée** par le provider, symbole inclus : lui seul sait en quelle monnaie sa source facture

#### Détection et fusion (`AiDetectionService`)
Fusion additive clé `Id`, **appelée uniquement sur ↻ Redétecter**, jamais en tâche de fond — même règle que les profils de navigateur.

- Masquage et ordre préservés ; nom personnalisé préservé (un nom égal à `DetectedName` suit l'outil)
- Un fournisseur absent des sondes est **conservé** avec `Detected = false` — le supprimer détruirait son masquage et son ordre pour une absence peut-être temporaire
- Une entrée inconnue du registre est conservée telle quelle : un retour arrière de version ne doit rien perdre
- `AiProbe.HiddenByDefault` n'agit **qu'à la découverte** : une redétection ne remasque jamais un fournisseur affiché
- **`LoadForStartup` détecte aussi quand le registre contient un fournisseur inconnu de la config** : c'est le cas d'une mise à jour qui apporte de nouveaux assistants. Sans ça ils n'apparaîtraient qu'après un ↻ Redétecter manuel, que personne ne pense à faire — la fonctionnalité serait livrée et invisible. Ce n'est pas une détection en tâche de fond : elle a lieu une fois, jusqu'à ce que la config rattrape le registre
- **Pas de « + Ajouter »** : un fournisseur exige une implémentation `IUsageProvider`, un ajout manuel donnerait une ligne vide

#### Fenêtre « Usage IA » (`UsageConfigDialog`)
☰ → Paramètres → **📊 Usage IA**. 620×660 redimensionnable, `MaxHeight` clampé sur la zone de travail, footer commun, sauvegarde immédiate (bouton **Fermer** seulement).

- Section **Bandeau** : afficher le bandeau, seuil d'alerte (`ComboBox` 5→50 % — `SettingsDialog` n'a aucun slider et dix valeurs discrètes se choisissent mieux dans une liste), afficher le coût, **fournisseur affiché** par défaut
- Section **Fournisseurs** : ↻ Redétecter, case de visibilité par ligne, pastille, chemin détecté, badges « masqué » / « non détecté » / « démo ». **Pas de ↑/↓** dans cette version : réordonner une liste d'une ligne est de l'UI morte — `Order` reste dans le modèle et dans la fusion, les boutons attendent qu'il y ait quelque chose à ordonner
- **La liste « Fournisseur affiché » propose *tous* les fournisseurs**, les masqués suffixés « (masqué) ». N'en lister que les visibles perdait le réglage en silence : masquer le fournisseur par défaut faisait retomber la liste sur « Premier disponible » à l'écran alors que le fichier gardait l'identifiant, et la modification suivante d'un autre réglage écrivait cette chaîne vide
- Toutes les écritures sont des load-modify-save sous `ConfigLock.Gate` : détection, cases de visibilité et réglages écrivent dans le même fichier
- **Un clic d'onglet dans le bandeau n'écrit rien** : le fournisseur du démarrage est le réglage `DefaultProviderId`, changer d'onglet est une sélection de session. La variante « dernier onglet cliqué » écrivait à chaque clic, donc en concurrence avec cette fenêtre ouverte

### Serveur MCP
- DockPad expose un serveur MCP permettant à Claude Code / Claude Desktop de piloter la grille, les pages et les navigateurs
- **Architecture** : Claude lance `DockPad.exe --mcp` — mode relais stdio (SDK officiel `ModelContextProtocol`), **aucune UI ni mutex**, détecté dans `App.xaml.cs` avant l'acquisition du mutex → chaque appel d'outil sérialise `{tool, args}` en JSON et l'envoie sur le named pipe `DockPad_McpPipe` (`McpPipeService`, multi-instances : Claude Code + Claude Desktop simultanés) → l'instance principale (déjà lancée par l'utilisateur) reçoit la requête : vérifie les options (`mcp.json`), exécute via les services d'actions **partagés avec l'UI** (`ShortcutActionService`, `PageActionService`, `BrowserActionService`), journalise (`McpLogService`), déclenche `RefreshGrid()` sur la grille si mutation, puis répond `{ok, data, error}` en une ligne
- **DockPad doit être lancé** — sinon le pipe est injoignable et l'outil renvoie une erreur explicite (« DockPad n'est pas lancé — démarre l'application pour utiliser ce serveur MCP »)
- **13 outils** `dockpad_<domaine>_<action>` (`Mcp/DockPadTools.cs`), positions **0-based** (page 0 = première page, lignes 0-3, colonnes 0-5) :
  - Grille : `grid_get`, `shortcut_add` (lot tout-ou-rien, position omise = première case libre), `shortcut_update`, `shortcut_move`, `shortcut_delete` 🔒
  - Pages : `page_add`, `page_update` (`iconPath` omis = inchangé, `""` = retirer l'icône ; `newIndex` = déplacement par insertion), `page_delete` 🔒
  - Navigateurs & règles : `browser_list`, `browser_update`, `rule_list`, `rule_add`, `rule_delete` 🔒 — `browser_list` expose `parentId`/`profileDirectory` (une entrée avec `parentId` est un profil, visable par une règle) ; `order` se compte dans la fratrie (parmi les navigateurs, ou parmi les profils d'un même navigateur)
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
    { "id": "d4e5f6", "name": "Boulot", "exePath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      "parentId": "a1b2c3", "profileDirectory": "Default", "detectedName": "Boulot", "order": 1 },
    { "id": "g7h8i9", "name": "Edge", "exePath": "…\\msedge.exe", "arguments": "", "order": 2 }
  ],
  "rules": [
    { "host": "github.com", "browserId": "a1b2c3" }
  ],
  "autoOpenSeconds": 5
}
```

- `id` : identifiant stable (8 hex aléatoires) référencé par les règles
- `parentId` + `profileDirectory` : présents uniquement sur un **profil** — l'entrée est lancée avec `--profile-directory="<profileDirectory>"` et s'affiche indentée sous son navigateur ; `detectedName` = nom lu dans le navigateur à la dernière détection (permet de préserver un nom personnalisé)
- `host` d'une règle : peut inclure un port (`localhost:44351`) — sans port, seul le port par défaut du scheme matche
- `autoOpenSeconds` : délai avant ouverture automatique avec le navigateur n°1 (0 = désactivé ; défaut et absent = 2 s)
- `iconPath` peut porter un index d'icône au format registre (ex : `"C:\\...\\chrome.exe,4"`)
- `arguments` : si elles contiennent `%1`, il est substitué par l'URL, sinon l'URL est ajoutée en fin
- `iconProfilePath` chemin relatif au profil (`%APPDATA%\DockPad\`), prioritaire sur `iconPath`
- `hidden` : masqué = absent de la popup mais conservé dans la config
- Stocké dans `%APPDATA%\DockPad\browsers.json`, inclus dans la sauvegarde de configuration

## Format JSON Usage IA

```json
{
  "enabled": true,
  "alertThreshold": 15,
  "showCost": true,
  "defaultProviderId": "claude",
  "providers": [
    { "id": "claude", "name": "Claude Code", "detectedName": "Claude Code",
      "hidden": false, "order": 0,
      "dataPath": "C:\\Users\\moi\\.claude\\projects", "detected": true },
    { "id": "demo", "name": "Démo", "detectedName": "Démo",
      "hidden": true, "order": 1, "detected": true }
  ]
}
```

- `alertThreshold` : pourcentage **restant** sous lequel une jauge passe au rouge (5 à 50, défaut 15)
- `defaultProviderId` : fournisseur affiché à l'ouverture. `""` = le premier visible. Un clic d'onglet **ne modifie pas** ce réglage
- `id` d'un fournisseur : clé de fusion, égale à `IUsageProvider.Id`. Volontairement **non `required`** dans le modèle — le type est désérialisé, et un `required` ferait échouer la lecture du fichier entier à cause d'une seule entrée abîmée. `UsageConfigService` écarte les entrées sans id et garde le reste
- `name` suit l'outil tant qu'il est égal à `detectedName` ; dès qu'il en diffère, il est considéré personnalisé et ne bouge plus
- `hidden` : absent du bandeau, conservé dans la config. `detected` à faux = conservé mais signalé « non détecté »
- Une entrée dont l'`id` est inconnu du registre est **conservée telle quelle** : un retour arrière de version ne perd ni masquage ni ordre
- Stocké dans `%APPDATA%\DockPad\usage.json`, inclus dans la sauvegarde de configuration

## Fenêtres de config — pattern commun

Toute fenêtre de config (Options, Navigateurs, Serveur MCP, Prédéfinis…) suit le même patron — **jamais de hauteur fixe sans clamp** (une hauteur codée en dur finit clippée quand le contenu grandit, ou déborde d'un écran 768p) :

- **Contenu statique** : `Width` fixe + `SizeToContent="Height"` + `MaxHeight="{Binding Source={x:Static SystemParameters.WorkArea}, Path=Height}"` — la fenêtre a la taille de son contenu, clampée à la zone de travail
- **Listes / onglets** (Navigateurs, Serveur MCP) : `ResizeMode="CanResize"`, taille initiale + `MinWidth`/`MinHeight`, même clamp `MaxHeight`, et les onglets à contenu statique enveloppés dans un `ScrollViewer`
- **Footer commun** (styles `DialogFooter` + `FooterVersion` dans App.xaml) : version à gauche (`TxtVersion.Text = AppInfo.VersionText`), boutons à droite — `Fermer` (SecondaryButton) pour les fenêtres à sauvegarde immédiate, Sauvegarder/Annuler pour les transactionnelles ; les actions spécifiques (Prédéfinis) restent à gauche après la version
- Header commun : `Border` bleu `#0078D4`, `Padding="20,14"`, titre blanc 16 SemiBold

## Profil DockPad (AppPaths)

Toutes les données utilisateur vivent dans un seul dossier, résolu par `AppPaths.ProfileRoot` : `shortcuts.json`, `pages.json`, `browsers.json`, `mcp.json`, `usage.json`, `icons\`, `logs\`, `.backup\`.

- Par défaut `%APPDATA%\DockPad`
- Surchargeable par la variable d'environnement **`DOCKPAD_PROFILE_DIR`** : le dossier indiqué est utilisé **tel quel** (aucun sous-dossier `DockPad` ajouté), chemin relatif accepté (rendu absolu), guillemets et espaces tolérés
- Résolue **une seule fois** au premier accès (`static readonly`) : modifier la variable en cours d'exécution n'a aucun effet — un outil qui la pose doit le faire avant tout appel aux services
- Usages : profil portable / configs de test, et les outils de capture qui travaillent sur un profil de fixture au lieu du profil réel
- Ne jamais reconstruire ces chemins à la main dans un service : `AppPaths.File("xxx.json")` ou `Path.Combine(AppPaths.ProfileRoot, …)`

## Captures d'écran de la doc (tools/McpShot, tools/BrowserShot)

Les captures du README vivent dans `docs/screenshots/`. Elles sont générées par de petits exes console qui instancient la **vraie** fenêtre hors process DockPad, avec les ressources App.xaml, puis rendent en `RenderTargetBitmap`. Ces outils servent aussi à **vérifier un rendu sans lancer DockPad**.

`tools/BrowserShot <cible> <cheminPng> [tabIndex]` — sélecteur de navigateur :

```bash
dotnet build tools/BrowserShot
BrowserShot.exe picker        docs/screenshots/browser-picker.png   # popup avec profils
BrowserShot.exe picker-header out.png                               # 1er navigateur masqué → titre de groupe
BrowserShot.exe config        docs/screenshots/browser-config.png 0 # onglet Navigateurs
BrowserShot.exe config        docs/screenshots/browser-rules.png  1 # onglet Règles de domaine
```

- **Profil de fixture** : l'outil pose `DOCKPAD_PROFILE_DIR` sur `%TEMP%\dockpad-browsershot` **avant** tout accès aux services, puis y écrit une config de démonstration (noms neutres : Boulot, Perso, Démo, Tests). Le profil réel de l'utilisateur n'est ni lu ni écrit — les captures ne contiennent aucune donnée personnelle et sont reproductibles
- `autoOpenSeconds = 0` pour la popup : un décompte ouvrirait vraiment un navigateur pendant la capture ; la fenêtre de config utilise 3 s (valeur d'affichage)
- L'état d'enregistrement affiché est celui de `BrowserShot.exe`, donc toujours « non enregistré » — c'est justement l'état initial décrit par le README
- La fenêtre de config est capturée en hauteur 840 (elle est redimensionnable) pour que liste et panneau d'édition tiennent ensemble, avec un profil sélectionné

`tools/UsageShot <cible> <cheminPng>` — bandeau Usage IA :

```bash
dotnet build tools/UsageShot
UsageShot.exe panel      docs/screenshots/usage-panel.png       # cas par defaut : un seul fournisseur
UsageShot.exe panel-tabs docs/screenshots/usage-panel-tabs.png  # deux fournisseurs, mecanique d'onglets
UsageShot.exe config     docs/screenshots/usage-config.png      # fenetre de reglages
UsageShot.exe window     docs/screenshots/usage-window.png      # integration dans la fenetre
UsageShot.exe window-off ...                                    # bandeau desactive : verifie qu'il ne laisse aucune place
UsageShot.exe panel-loading ...                                 # etat d'attente : sablier a la place de la pastille
UsageShot.exe panel-idle ...                                    # fournisseur detecte mais inactif : onglet a zero
UsageShot.exe panel-quota docs/screenshots/usage-panel-quota.png # quota refuse : la notice a la place des jauges
UsageShot.exe window-unlocked ...                               # verrou des tuiles ouvert : bouton en coche bleue
```

- **Les cibles `window*` écrivent aussi une grille de démonstration** dans le profil de fixture : sans elle la fenêtre se juge sur une grille vide, qui ne montre ni les icônes, ni les bandes de couleur des types, ni la pagination — donc rien de ce qui fait l'application. Dix-neuf tuiles des cinq types sur trois pages, et quelques cases laissées vides parce que le `+` grisé fait partie de ce qu'il faut montrer. Les icônes viennent de deux sources, dans cet ordre : le jeu de PNG du projet (`C:\dev\Dock-icons`, surchargeable par `DOCKPAD_DEMO_ICONS`) puis l'icône de l'exécutable, quand il est réellement installé — même patron de candidats que `PresetService`. **Rien n'est embarqué dans le dépôt** : ce sont des logos de produits, qu'on ne redistribue pas dans un dépôt public. Sur une machine sans ce dossier ni ces applications, la tuile s'affiche sans icône : la capture est moins jolie, elle n'est pas cassée. Les tuiles `OpenUrl` sans logo retombent sur l'icône du navigateur, ce qui est exactement le comportement de l'application. L'icône dossier par défaut étant une ressource **embarquée**, donc absente du disque, le csproj de l'outil la copie à côté de l'exe : sans ça les tuiles `OpenFolder` s'affichaient sans icône
- **Fixture exclusivement `DemoUsageProvider`** (quatre instances), `ClaudeUsageProvider` délibérément absent : les vrais chiffres de consommation sont des données personnelles, et une capture doit être reproductible. C'est la raison d'être de la liste injectable de `UsageService` — le registre de production reste intact
- La fixture déclare un fournisseur masqué et un non détecté, pour que les badges apparaissent sur la capture de la fenêtre de réglages : la machine de développement ne produit pas ces états d'elle-même
- **Les cibles `panel` et `window` sont rendues hors écran** (`Measure`/`Arrange` explicites, sans afficher de fenêtre) : une fenêtre à `SizeToContent` mesure avant que les données arrivent et ne reprend pas la hauteur ensuite, et `Show()` sur `QuickAccessWindow` déclenche la boucle décrite plus bas
- **Deux passages de mise en page pour `window`** : la géométrie du bandeau est posée au premier `LayoutUpdated`, donc après la première mesure. Avec un seul passage, la hauteur retenue est celle d'avant et la barre de pagination sort de l'image
- **`Start()` avant toute mesure** : sans données le bandeau est `Collapsed` et la capture est vide
- **`app.ico` doit être liée dans le csproj de l'outil** (`<Resource Include="..\..\app.ico" Link="app.ico" />`) : `QuickAccessWindow` la référence par pack URI, qui se résout dans l'assembly **hôte**. Sans elle, la cible `window` lève une `IOException` au chargement du XAML. Les cibles de `BrowserShot` n'en ont pas besoin, ce sont des dialogs sans icône
- **Rendre l'élément de contenu, pas le `Window`** : sur une fenêtre sans chrome (`WindowStyle=None`) le rendu du `Window` lui-même sort transparent
- **Largeur de capture = 698 px**, la largeur réelle du bandeau : le bloc de tuiles (6 × 118) moins les marges horizontales d'une tuile. Capturer plus large donne une image flatteuse et fausse — c'est à cette largeur que les jauges se serrent
- **La fixture n'expose que deux fournisseurs visibles**, et un seul pour la cible `panel`. Quatre onglets écrasaient les jauges à la largeur réelle ; et le cas par défaut n'a de toute façon qu'un fournisseur

> **La cible `window` ne doit jamais appeler `Show()`.** Affichée dans un hôte qui n'est pas une vraie application, `QuickAccessWindow` déclenche une boucle qui affame le dispatcher : le processus tourne à 80 % d'un cœur et rien ne s'exécute plus — ni `ContentRendered`, ni un `DispatcherTimer` posé après `Show`. Reproduit y compris à un commit où cette cible produisait encore une image correcte, donc ce n'est pas la mise en page. Le contournement est de **mesurer et arranger son contenu hors écran**, comme la cible `panel` : l'instanciation de la fenêtre est évitée et le rendu est immédiat.

`tools/DialogShot <fenêtre> <fr|en> <cheminPng>` — n'importe quelle fenêtre, dans une langue donnée :

```bash
dotnet build tools/DialogShot
DialogShot.exe settings en out.png      # Options en anglais
DialogShot.exe ctxmenu fr out.png       # gestionnaire de menu contextuel
```

Rendu **hors écran** (`Measure`/`Arrange`, jamais `Show()`), profil de fixture via
`DOCKPAD_PROFILE_DIR`. Les outils plus anciens (`UsageShot`, `BrowserShot`) acceptent la variable
d'environnement **`DOCKPAD_SHOT_LANG`** — une variable plutôt qu'un argument, pour ne pas déplacer
les arguments de leurs cibles.

`tools/McpShot <tabIndex> <cheminPng>` — fenêtre Serveur MCP, avec des entrées de journal de démonstration et les chemins `C:\DockPad` dans les commandes affichées :

```bash
dotnet build tools/McpShot
tools/McpShot/bin/Debug/net8.0-windows/McpShot.exe 0 docs/screenshots/mcp-options.png   # onglet Options
tools/McpShot/bin/Debug/net8.0-windows/McpShot.exe 1 docs/screenshots/mcp-journal.png   # onglet Journal
```

À régénérer après tout changement visuel des fenêtres concernées. **Les captures sont validées (et floutées si besoin) avant push.**

Pièges WPF contournés dans ces outils — à connaître avant de les étendre à d'autres fenêtres :
- `[STAThread]` obligatoire (les top-level statements ne l'appliquent pas)
- Ressources App.xaml : `new App()` + `app.InitializeComponent()` **sans** `Run()` (sinon `OnStartup` exécute mutex/systray/fenêtres) ; `ShutdownMode = OnExplicitShutdown`
- `ShowDialog()` retourne immédiatement dans ce contexte (Application jamais `Run`) → `Show()` + `Dispatcher.Run()`, arrêt par `Dispatcher.InvokeShutdown()`
- Pas d'`await` dans les handlers : sans `SynchronizationContext`, la continuation part sur le thread pool → `DispatcherTimer`
- `PresentationSource.FromVisual` est nul ici → échelle DPI via `VisualTreeHelper.GetDpi`
- Sélectionner l'onglet du `TabControl` **avant** `Show()` (une bascule post-rendu ne se répercute pas)
- Un seul objet `Application` par processus : un process de capture par fenêtre/onglet
- `DockPad.csproj` exclut `tools\**` de son glob de compilation (projet frère dans un sous-dossier, comme `DockPad.Tests`)
- Le binaire `DockPad.exe` est verrouillé par une instance en cours : **fermer DockPad avant `dotnet build`**, sinon MSB3021/MSB3027 (l'outil de capture référence `DockPad.csproj`)

> Une **valeur locale** (attribut posé sur l'élément, ex. `Visibility="Collapsed"`) bat les `Setter` des `DataTrigger` d'un `Style` : elle ne sera jamais remplacée. Mettre la valeur par défaut dans un `<Setter>` du `Style` et laisser les triggers la surcharger — sinon le trigger paraît « ne rien faire » (cas vécu sur le filet d'indentation des profils de navigateur).

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
