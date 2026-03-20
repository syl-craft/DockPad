# DockPad

Application WPF C# (.NET 8, x64) pour gérer le menu clic droit Windows via le registre.
L'app démarre sans droits admin — l'élévation est demandée à la demande via un bouton UAC.

## Stack

- **WPF / .NET 8**  `net8.0-windows`, `UseWPF=true`, `UseWindowsForms=true` (pour NotifyIcon)
- **Registre**  lecture/écriture via `Microsoft.Win32.Registry`
- **Icônes**  `System.Drawing.Common` (NuGet) pour extraire les icônes `.exe`/`.dll`
- **JSON**  `System.Text.Json` (built-in) pour la config des raccourcis rapides

## Structure

```
App.xaml/.cs                             Point d'entrée : instance unique (Mutex), NotifyIcon systray
GlobalUsings.cs                          Usings globaux
DockPad.csproj / DockPad.sln

Converters/
    InverseBoolConverter.cs              Converter WPF bool inversé

Models/
    ContextMenuEntry.cs                  Modèle de données + enum ContextMenuTarget
    ContextMenuEntryViewModel.cs         VM avec chargement d'icône (BitmapSource)
    PageConfig.cs                        Config par page (icône du bouton de pagination)
    PresetEntry.cs                       Modèle preset avec enum PresetStatus
    ShortcutEntry.cs                     Modèle raccourci rapide (page, row, col, name, type, command, iconPath)
    TerminalConfig.cs                    Config d'un terminal (exePath, startingDirectory, runCommand…)
    TerminalInfo.cs                      Informations d'un terminal détecté

Services/
    HotkeyService.cs                     P/Invoke RegisterHotKey / UnregisterHotKey (user32.dll)
    PageConfigService.cs                 Load/Save pages.json (%APPDATA%\DockPad\pages.json)
    PresetService.cs                     Raccourcis prédéfinis (Claude, PowerShell, VS Code, SSMS)
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
- Bande colorée (4px, droite) indique le type : bleu=RunCommand, ambre=OpenFolder, vert=OpenUrl, violet=OpenTerminal
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

### Types de tuiles (ShortcutType)
| Type | Description | Champ `command` |
|------|-------------|-----------------|
| `RunCommand` | Lance un exécutable ou une commande shell | ex: `notepad.exe`, `"C:\app.exe" arg` |
| `OpenFolder` | Ouvre un dossier dans l'Explorateur | chemin du dossier |
| `OpenUrl` | Ouvre une URL dans le navigateur par défaut | URL complète |
| `OpenTerminal` | Ouvre un terminal dans un dossier (wt → pwsh → powershell → cmd) | chemin du dossier |

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
- Affiche la version de l'application (ex: `v1.3.0`) en bas à gauche du footer, lue depuis `Assembly.GetExecutingAssembly()`

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
  { "row": 0, "col": 0, "name": "Mon app",    "type": "RunCommand",   "command": "explorer.exe \"C:\\dev\\projet\"", "iconPath": "C:\\...\\icon.png" },
  { "row": 0, "col": 1, "name": "C:\\dev",    "type": "OpenFolder",   "command": "C:\\dev",                         "iconPath": "C:\\Windows\\explorer.exe" },
  { "row": 0, "col": 2, "name": "GitHub",     "type": "OpenUrl",      "command": "https://github.com",              "iconPath": "" },
  { "row": 0, "col": 3, "name": "Terminal",   "type": "OpenTerminal", "command": "C:\\dev",                         "iconPath": "" }
]
```

Le champ `type` est optionnel — une entrée sans `type` utilise `RunCommand` (rétrocompatible).
Le champ `terminal` est optionnel et uniquement présent pour le type `OpenTerminal`.
Les colonnes vont de 0 à 5, les lignes de 0 à 3. Le champ `page` commence à 0.

## Icône de l'application

Fichier : `app.ico` (multi-taille : 16/32/48/256px)

Source : **Microsoft Fluent UI System Icons** — `ic_fluent_apps_list_32_color.svg`
- Page GitHub : `https://github.com/microsoft/fluentui-system-icons/blob/main/assets/Apps%20List/SVG/ic_fluent_apps_list_32_color.svg`
- Raw SVG : `https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/assets/Apps%20List/SVG/ic_fluent_apps_list_32_color.svg`
- Licence : MIT

Configuré dans :
- `.csproj` → `<ApplicationIcon>app.ico</ApplicationIcon>` (icône du `.exe`)
- `QuickAccessWindow.xaml` → `Icon="pack://application:,,,/app.ico"`
- `ContextMenuManagerWindow.xaml` → `Icon="pack://application:,,,/app.ico"`

> Toujours utiliser le pack URI (`pack://application:,,,/app.ico`) pour référencer `app.ico` dans les XAML situés dans des sous-dossiers, sinon WPF résout le chemin relatif depuis le sous-dossier (ex: `views/app.ico`) et lève une `IOException`.

> Tout `Border` avec un `CornerRadius` non nul doit avoir `SnapsToDevicePixels="True"` et `UseLayoutRounding="True"` pour éviter le rendu flou des bords arrondis (sub-pixel rendering WPF).

Icônes des tuiles (PNG 64×64) stockées dans `C:\dev\Dock-icons\`, sources :
- **Fluent UI System Icons** (Microsoft) — dossier, task-board
- **PKief/vscode-material-icon-theme** — claude, azure, pipeline
- **devicons/devicon** — visual-studio, vscode, fusion360
- **BambuLab/BambuStudio** (GitHub officiel) — bambu-studio
- **Simple Icons** — gmail, azure-devops
- **GitHub Octicons** — pull-requests

## Versioning

Version semver définie dans `DockPad.csproj` : `<Version>1.3.0</Version>`

Pour bumper la version, modifier ce champ puis commit + publish.

## Build

```bash
dotnet build

# Publish via le profil (méthode préférée)
dotnet publish -p:PublishProfile=FolderProfile
```

Le publish via `FolderProfile` :
1. Compile en Release framework-dependent (requiert .NET 8 sur la machine cible)
2. Crée un zip `release\DockPad-{version}.zip`
3. Supprime le dossier `publish\` intermédiaire

Après publish, déployer le zip ou copier les fichiers vers `C:\Users\Sylvain\Documents\Afiliza@Drive\DockPad\`.
