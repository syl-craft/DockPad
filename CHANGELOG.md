# Changelog

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
