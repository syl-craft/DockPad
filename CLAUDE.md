# DockPad

Application WPF C# (.NET 8, x64) pour gérer le menu clic droit Windows via le registre.
L'app démarre sans droits admin — l'élévation est demandée à la demande via un bouton UAC.

## Stack

- **WPF / .NET 8**  `net8.0-windows`, `UseWPF=true`, `UseWindowsForms=true` (pour NotifyIcon)
- **Registre**  lecture/écriture via `Microsoft.Win32.Registry`
- **Icônes**  `System.Drawing.Common` (NuGet) pour extraire les icônes `.exe`/`.dll`
- **JSON**  `System.Text.Json` (built-in) pour la config des raccourcis rapides
- **WMI**  `System.Management` (NuGet) pour lire la ligne de commande des processus (`SwitchToProcess`)

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
    ContextMenuEntry.cs                  Modèle de données + enum ContextMenuTarget
    ContextMenuEntryViewModel.cs         VM avec chargement d'icône (BitmapSource)
    PageConfig.cs                        Config par page (icône du bouton de pagination + IconProfilePath)
    PresetEntry.cs                       Modèle preset avec enum PresetStatus
    ProcessSwitchConfig.cs               Config SwitchToProcess (processName, executable, parameters)
    ShortcutEntry.cs                     Modèle raccourci rapide (page, row, col, name, type, command, iconPath, iconProfilePath)
    TerminalConfig.cs                    Config d'un terminal (exePath, startingDirectory, runCommand…)
    TerminalInfo.cs                      Informations d'un terminal détecté

Services/
    HotkeyService.cs                     P/Invoke RegisterHotKey / UnregisterHotKey (user32.dll)
    IconCacheService.cs                  Cache d'icônes dans %APPDATA%\DockPad\icons\ (SHA1 dédup, extraction .exe/.dll → .png)
    PageConfigService.cs                 Load/Save pages.json (%APPDATA%\DockPad\pages.json)
    PresetService.cs                     Raccourcis prédéfinis (Claude, PowerShell, VS Code, SSMS)
    ProcessSwitchService.cs              SwitchOrLaunch : cherche via WMI, SetForegroundWindow ou lance l'exe
    RegistryService.cs                   CRUD registre (HKCR / HKCU / HKLM)
    ResourceStringResolver.cs            Résolution des @dll,-id via SHLoadIndirectString
    SettingsService.cs                   Lecture/écriture paramètres HKCU + autostart
    ShortcutService.cs                   Load/Save shortcuts.json (%APPDATA%\DockPad\shortcuts.json)
    TerminalDetectionService.cs          Détection des terminaux installés + construction des arguments

Views/
    ContextMenuManagerWindow.xaml/.cs    Gestion des entrées de menu contextuel Windows
    QuickAccessWindow.xaml/.cs           Grille de tuiles multi-pages (hotkey global)

Dialogs/
    AppDialog.xaml/.cs                   Dialog custom styled (remplace MessageBox) — Confirm/Error/Warning/Info
    EntryDialog.xaml/.cs                 Ajout/modification d'une entrée de menu contextuel (registre)
    PresetsDialog.xaml/.cs               Raccourcis prédéfinis
    SettingsDialog.xaml/.cs              Configuration du raccourci clavier global + démarrage auto + version
    ShortcutDialog.xaml/.cs              Ajout/modification d'une tuile d'accès rapide

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
  - *Paramètres* : ⚙ Options
  - *Configuration* : ↺ Actualiser, ✎ Modifier, 💾 Sauvegarder, 📁 Voir le dossier
  - ✕ Quitter l'application
- **Raccourci clavier actif** affiché en bas à droite (badge `Consolas`, mis à jour après changement dans Options)
- **Sauvegarder la configuration** : copie `shortcuts.json` et `pages.json` dans `%APPDATA%\DockPad\.backup\` avec horodatage
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

### Raccourcis clavier par chiffre (overlay)
- Appuyer sur **Ctrl** seul → overlay numéroté sur les 3×3 tuiles gauches ; **Shift** seul → tuiles droites
- **Ctrl + 1-9** ou **Shift + 1-9** exécute directement la tuile correspondante
- L'overlay se masque à la désactivation de la fenêtre ou à la frappe Échap
- Les modificateurs trigger s'adaptent automatiquement au raccourci global configuré

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
- **Démarrer avec Windows** : checkbox qui ajoute/supprime une entrée dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- Affiche le chemin de l'exécutable utilisé pour la clé de démarrage automatique
- Affiche la version de l'application (ex: `v1.5.1`) en bas à gauche du footer, lue depuis `Assembly.GetExecutingAssembly()`
- **Claude Code — Arguments supplémentaires** : champ texte libre pour passer des options à `claude` (ex: `--enable-auto-mode`), stocké dans `HKCU\Software\DockPad\Settings\ClaudeArgs`, appliqué au prédéfini "Ouvrir un terminal Claude"

## Prédéfinis

| Nom | Cible | Commande |
| ----- | ------- | --------- |
| Ouvrir un terminal Claude | FolderBackground | `wt.exe -w 0 new-tab --startingDirectory "%V" -- claude` |
| Ouvrir dans PowerShell | FolderBackground | `wt.exe -w 0 new-tab --startingDirectory "%V"` (pwsh/powershell fallback) |
| Ouvrir dans Visual Studio Code | FolderBackground | `code "%V"` |
| Ouvrir dans SQL Server Management Studio | FolderBackground | `ssms.exe "%V"` |

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

## Versioning

Version semver définie dans `DockPad.csproj` : `<Version>1.5.4</Version>`

Pour bumper la version, modifier ce champ puis commit + publish.

## Build

```bash
dotnet build

# Publish via le profil (méthode préférée)
dotnet publish -p:PublishProfile=FolderProfile
```

Le publish via `FolderProfile` :
1. Compile en Release framework-dependent (requiert .NET 8 sur la machine cible)
2. Copie `CHANGELOG.md` dans le dossier publish
3. Crée `release\DockPad-{version}.zip` et `release\DockPad-{version}-Changelog.md`
4. Supprime le dossier `publish\` intermédiaire

Après publish, vider `C:\DockPad\` et extraire le zip dedans.
