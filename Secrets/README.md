# `Secrets/` — injection de secrets depuis Vaultwarden

> Ce dossier est un **périmètre d'audit**, pas un rangement. Le relire en entier, c'est avoir vu
> tout le code de DockPad qui manipule un secret.

## L'invariant

**Tout ce qui voit un secret vit ici. Rien d'autre n'y vit, et rien hors d'ici n'en voit.**

Quatre matières ne franchissent jamais cette frontière :

1. le mot de passe maître du coffre ;
2. la clé de session rendue par `bw unlock` ;
3. les valeurs lues dans le coffre ;
4. le texte rendu.

`AppSettings` porte trois réglages de la fonctionnalité (chemin de `bw.exe`, délai d'effacement,
organisation) et vit **dehors** : ce sont des préférences — un chemin, un nombre, un nom — jamais de
la matière secrète.

## La surface d'entrée

Deux types publics, et c'est tout le couplage avec le reste de l'application :

| Appel | Depuis |
|---|---|
| `SecretInjection.Handle(chemin)` | `App.xaml.cs` — clic droit relayé par le pipe |
| `SecretInjection.IsClipboardArmed` / `ClipboardChanged` | `App.xaml.cs` — sortie différée de l'instance éphémère |
| `SecretInjection.ClearClipboardNow()` | `App.OnExit` — filet de sortie |
| `SecretMenu.IsInstalled / Install / Uninstall` | `SettingsDialog` |

DockPad est un assembly unique : `internal` ne peut pas poser cette frontière. C'est
`DockPad.Tests/Secrets/SecretBoundaryGuardTests.cs` qui la tient.

## Les trois gardes

Vérifiés par mutation — on introduit la violation, on regarde le test tomber, on la retire.

| Garde | Interdit | Prouve |
|---|---|---|
| **Frontière** | Nommer un type d'ici hors des points d'entrée déclarés | La surface ne grandit pas en douce |
| **Rien sur disque** | `File.Write*`, `FileStream` en écriture, `StreamWriter` ici | La garantie centrale, par le code et non par relecture |
| **Rien en ligne de commande** | `--session`, `--password`, et tout identifiant sentant le secret dans une collection d'arguments | Ni le mot de passe ni la clé ne sont lisibles des autres processus |

La troisième **durcit** le script PowerShell d'origine, qui passait `--session $env:BW_SESSION` en
argument — donc lisible par tout processus, y compris par la lecture WMI que DockPad fait lui-même
pour `SwitchToProcess`.

## La garantie centrale : tout ou rien

Si un seul marqueur ne se résout pas, **rien** ne va dans le presse-papier et rien n'est écrit. Un
rendu partiel est pire que pas de rendu : c'est la panne d'origine, une stack déployée avec ses
marqueurs `REMPLACER` jamais remplacés.

Deux filets, et le second ne fait pas confiance au premier :

1. chaque marqueur non résolu est collecté, et un seul annule tout ;
2. le texte produit est balayé, et tout `{{ … }}` ou `REMPLACER` survivant le rejette — y compris
   venu d'une valeur du coffre.

`SecretRenderResult` est *soit* un texte, *soit* une liste d'échecs. Lire `Text` sur un échec lève.

**Conséquence assumée du second filet** : il rejette *tout* `{{ … }}`, y compris un gabarit Go ou
Jinja légitime qui cohabiterait dans le fichier. Il ne sait pas distinguer, et il se trompe du bon
côté.

## Syntaxe des marqueurs

```
{{ bw:<nom-de-l-item>:<champ> }}
```

`<champ>` est cherché d'abord parmi les **champs personnalisés** de l'item, puis parmi `password`,
`username`, `notes`, `totp`. Un champ personnalisé vide ne retombe pas sur le champ standard du même
nom : il existe, il est vide, et on le dit.

**En YAML, toujours placer un marqueur à l'intérieur d'une chaîne entre guillemets** — une accolade
double en début de valeur serait lue comme un dictionnaire.

Piège de stockage : Docker Compose interprète les `$`, et un hash bcrypt en est plein. **Stocker la
forme déjà échappée**, chaque `$` doublé, et le dire dans le nom du champ.

## Ce qui traverse le dossier

```
clic droit  →  App  →  SecretInjection.Handle
                          └─ SecretInjectionWindow    vérification → déverrouillage → travail → compte-rendu
                               ├─ SecretInjectionService   status · unlock · organizations · items
                               │    ├─ BitwardenCli        le seul point qui lance bw.exe
                               │    ├─ SecretVault         PUR — un item, aucun, ou deux
                               │    │    └─ SecretFieldResolver   PUR — l'ordre des champs
                               │    └─ SecretTemplate      PUR — marqueurs, substitution, deux filets
                               └─ ClipboardGuard           copie marquée, empreinte, minuteur
```

## Le presse-papier

Trois formats enregistrés, documentés par Microsoft sous *Cloud Clipboard and Clipboard History
Formats*, excluent le contenu de `Win+V` et de la synchronisation entre appareils :
`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` et
`CanUploadToCloudClipboard`, ces deux derniers avec un **DWORD sérialisé à zéro**.

**Un `MemoryStream` de quatre octets, jamais un `int`** : `DataObject.SetData` sérialisait par
`BinaryFormatter`, désactivé depuis .NET 8. Un test le fige — sinon une montée de version casserait
la protection en silence.

L'effacement est **gardé** : on retient l'empreinte SHA-256 du texte, jamais le texte, et on
n'efface que si le presse-papier porte encore exactement ce qu'on y a mis. Sinon l'utilisateur a
copié autre chose entre-temps, et l'effacer détruirait ses données — c'est ce que fait KeePass.

Un délai réglé à **zéro désarme le verrou entier** : aucune empreinte retenue, et la sortie de
l'application n'efface rien. Un réglage qui dit « ne pas effacer » ne peut pas effacer quand même à
la fermeture.

## Le journal

Le nom du fichier, le nombre de marqueurs, le nombre d'items, et en cas d'échec les **noms** des
marqueurs fautifs. La règle de partage est le flux, pas le jugement au cas par cas : `stdout` de
`bw` porte les données du coffre et n'est **jamais** journalisé ni affiché ; `stderr` et le code de
sortie sont des diagnostics et vont au journal.

## Première configuration

```powershell
winget install Bitwarden.CLI                                  # jamais par npm
bw config server https://vaultwarden.beagle-draco.ts.net
bw login                                                      # mot de passe maître + TOTP
```

**Par winget, pas par npm** : winget prend le binaire des releases GitHub officielles de Bitwarden
et en vérifie l'empreinte, là où une installation npm expose en plus à toute la chaîne de
dépendances transitives. Le client de bureau Bitwarden ne fournit **pas** la CLI : ce sont deux
produits distincts.

Vaultwarden n'ayant qu'un coffre par compte, la séparation se fait par **organisation** : une
organisation dédiée évite qu'un item personnel du même nom rende la résolution ambiguë. Elle se
règle dans ☰ → Paramètres → Options.
