# WinContextMenuManager

Application WPF C# (.NET 8, x64) pour gérer le menu clic droit Windows via le registre.
L'app démarre sans droits admin — l'élévation est demandée à la demande via un bouton UAC.

## Stack

- **WPF / .NET 8**  `net8.0-windows`, `UseWPF=true`, `UseWindowsForms=true` (pour NotifyIcon)
- **Registre**  lecture/écriture via `Microsoft.Win32.Registry`
- **Icônes**  `System.Drawing.Common` (NuGet) pour extraire les icônes `.exe`/`.dll`
- **JSON**  `System.Text.Json` (built-in) pour la config des raccourcis rapides

## Structure

```
Models/
    ContextMenuEntry.cs                  Modèle de données + enum ContextMenuTarget
    ContextMenuEntryViewModel.cs         VM avec chargement d'icône (BitmapSource)
    PresetEntry.cs                       Modèle preset avec enum PresetStatus
    ShortcutEntry.cs                     Modèle raccourci rapide (row, col, name, command, iconPath)
Services/
    RegistryService.cs                   CRUD registre (HKCR / HKCU / HKLM)
    PresetService.cs                     Raccourcis prédéfinis (Claude, PowerShell, VS Code, SSMS)
    ResourceStringResolver.cs           Résolution des @dll,-id via SHLoadIndirectString
    HotkeyService.cs                     P/Invoke RegisterHotKey / UnregisterHotKey (user32.dll)
    SettingsService.cs                   Lecture/écriture des paramètres dans HKCU + autostart
    ShortcutService.cs                   Lecture du fichier JSON de raccourcis rapides
App.xaml/.cs                             Point d'entrée : instance unique (Mutex), NotifyIcon systray
ContextMenuManagerWindow.xaml/.cs        Gestion des entrées de menu contextuel Windows
QuickAccessWindow.xaml/.cs               Grille 4×6 de raccourcis rapides (hotkey global)
SettingsDialog.xaml/.cs                  Configuration du raccourci clavier global + démarrage auto
EntryDialog.xaml/.cs                     Dialogue ajout/modification d'une entrée
PresetsDialog.xaml/.cs                   Dialogue raccourcis prédéfinis
InverseBoolConverter.cs                  Converter WPF bool inversé
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
- Raccourcis prédéfinis avec détection de mise à jour (comparaison commande + icône)
- **Élévation à la demande** : bouton `🛡 Élever` + bandeau jaune si non-admin, relance l'app en admin via UAC

### Accès rapide (QuickAccessWindow)
- Grille 4 lignes × 6 colonnes de tuiles cliquables
- Chaque tuile : icône + nom → exécute la commande au clic
- Icônes supportées : `.exe`, `.dll`, `.ico`, `.png`, `.bmp`, `.jpg`
- Cases vides affichées en `+` grisé
- Bouton **Actualiser** (relit le JSON) + **Modifier la configuration** (ouvre le JSON)
- Config stockée dans `%APPDATA%\WinContextMenuManager\shortcuts.json`

### Raccourci clavier global
- Hotkey configurable via `SettingsDialog` (Ctrl/Alt/Shift/Win + touche A-Z ou F1-F12)
- Défaut : `Ctrl+Shift+M`
- Affiche `QuickAccessWindow` (la remet au premier plan si déjà visible)
- Config stockée dans `HKCU\Software\WinContextMenuManager\Settings`
- Enregistrement géré par `QuickAccessWindow`

### Paramètres (SettingsDialog)
- Configuration du raccourci clavier global
- **Démarrer avec Windows** : checkbox qui ajoute/supprime une entrée dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- Affiche le chemin de l'exécutable utilisé pour la clé de démarrage automatique

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
  { "row": 0, "col": 0, "name": "Mon app", "command": "explorer.exe \"C:\\dev\\projet\"", "iconPath": "C:\\...\\icon.png" }
]
```

Les cases non définies s'affichent vides. Les colonnes vont de 0 à 5, les lignes de 0 à 3.

## Build

```bash
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

Le `.exe` publié (~155 Mo) est autonome, sans dépendance .NET externe.
