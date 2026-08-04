# DockPad

Application WPF (.NET 8, x64) de **barre de lancement rapide** avec gestion du menu contextuel Windows.

## Fonctionnalités

- **Grille de tuiles** multi-pages (4 × 6) avec raccourci clavier global configurable
- **Types de raccourcis** : lancer une commande, ouvrir un dossier, URL, terminal, basculer vers un processus
- **Drag & drop** depuis l'Explorateur Windows (dossier → OpenFolder, fichier .url → OpenUrl)
- **Barre de recherche** globale avec navigation clavier
- **Overlay numérique** (Ctrl/Shift + 1–9) pour exécution rapide au clavier
- **Cache d'icônes** portable dans `%APPDATA%\DockPad\icons\`
- **Gestionnaire de menu contextuel** Windows (HKCU / HKLM / HKCR)
- **Raccourcis prédéfinis** : Claude Code, PowerShell, VS Code, SSMS, GitHub Desktop
- **Sélecteur de navigateur** : popup de choix au clic sur une URL + règles par domaine
- **Icône systray** — l'application tourne en arrière-plan, instance unique (Mutex)
- **Démarrage automatique** avec Windows configurable

## Sélecteur de navigateur

DockPad peut devenir le navigateur par défaut de Windows : au clic sur une URL, une popup propose le choix du navigateur (profils et navigation privée possibles via les arguments). Les règles « Toujours pour ce domaine » ouvrent les sites connus directement, sans popup (sous-domaines inclus).

| Popup au clic sur une URL | Configuration |
|:---:|:---:|
| ![Popup de choix](docs/screenshots/browser-picker.png) | ![Configuration des navigateurs](docs/screenshots/browser-config.png) |

Clavier : `1-9` choix direct · `↑/↓` + `Entrée` · `Échap` annule · perte de focus = annule.

### Activer sur un ordinateur

- [ ] Lancer DockPad
- [ ] **☰ → Paramètres → 🌐 Navigateurs** → vérifier la liste détectée (Chrome, Edge…)
- [ ] Cliquer **S'enregistrer comme navigateur**
- [ ] Cliquer **Paramètres Windows…** → définir **DockPad** comme navigateur par défaut
- [ ] Cliquer une URL n'importe où → la popup s'affiche ; cocher **Toujours pour ce domaine** pour créer une règle
- [ ] Gérer les règles dans l'onglet **Règles de domaine** (recherche, filtre, réassociation, suppression)

## Prérequis

- Windows 10/11 x64
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Desktop)

## Installation

1. Télécharger `DockPad-{version}.zip` depuis `release\`
2. Extraire dans `C:\DockPad\`
3. Lancer `DockPad.exe`

## Build

```bash
dotnet build
```

### Publish (release)

```bash
dotnet publish -p:PublishProfile=FolderProfile
```

Génère `release\DockPad-{version}.zip` et `release\DockPad-{version}-Changelog.md`.

## Configuration

Les fichiers de configuration sont dans `%APPDATA%\DockPad\` :

| Fichier | Contenu |
|---------|---------|
| `shortcuts.json` | Tuiles de la grille de raccourcis |
| `pages.json` | Configuration des boutons de pagination |
| `browsers.json` | Navigateurs du sélecteur + règles de domaine |
| `icons\` | Cache d'icônes (PNG, déduplication SHA1) |
| `.backup\` | Sauvegardes horodatées |

Les paramètres (hotkey, démarrage auto) sont stockés dans `HKCU\Software\DockPad\Settings`.

## Raccourci clavier par défaut

`Ctrl + Shift + M` — affiche/remet au premier plan la fenêtre principale.
Configurable via **☰ Menu → Options**.
