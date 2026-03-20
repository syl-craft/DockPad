# Changelog

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
