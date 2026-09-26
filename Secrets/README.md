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
| **Charge utile par stdin** | le JSON d'un item dans une collection d'arguments | les valeurs saisies ne sont pas lisibles des autres processus |

La troisième **durcit** le script PowerShell d'origine, qui passait `--session $env:BW_SESSION` en
argument — donc lisible par tout processus, y compris par la lecture WMI que DockPad fait lui-même
pour `SwitchToProcess`.

## Rendu au mieux, et l'écran qui ne ment pas

C'était **tout ou rien**. La règle a été renversée : une clé absente du coffre n'annule plus les
autres. Ce qui la remplace n'est pas rien — le risque réel n'a jamais été qu'un rendu soit partiel,
mais qu'il **ait l'air complet**. La panne d'origine, c'est une stack déployée avec ses `REMPLACER`
parce que personne ne les a vus.

Une seule chose se dégrade : **le coffre qui répond « je ne l'ai pas »**. C'est une donnée sur le
coffre, elle est listée et on continue. CLI absente, déverrouillage refusé, fichier illisible, deux
annotations visant le même fichier : ce sont des erreurs, et elles refusent toujours.

Trois choses tiennent la garantie à sa place :

1. **un marqueur non résolu reste littéral** — il est sa propre trace, visible dans ce qu'on colle ;
2. **un fichier de secret n'est jamais écrit vide** — `containerboot` lit `TS_AUTHKEY` sans rien
   roger : ne pas écrire est bruyant, écrire du vide est silencieux ;
3. **n'avoir rien résolu du tout reste un échec** — c'est le dernier garde, et celui qui compte.

L'écran porte le reste : un état **incomplet** en ambre, distinct du vert, qui liste les clés
absentes, les fichiers écrits et les fichiers périmés — et qui, seul de tous, **ne se referme pas
tout seul**.

### On nomme ce qui vient du fichier, on compte ce qui vient du coffre

Le second filet ne veto plus, mais il n'a rien perdu de son rôle : il rapporte ce qu'il ne
**connaît** pas. Une clé absente et `REMPLACER` viennent du gabarit — on les nomme. Un `{{ … }}`
venu d'une **valeur du coffre** reste **compté, jamais recopié** : ce serait un morceau de secret à
l'écran. Un test le vérifie au milieu de clés manquantes, parce que c'est la fuite que ce
relâchement pourrait ouvrir sans qu'on la voie.

### `template:` — un modèle rendu plutôt qu'une valeur

Une annotation `x-bw` porte **soit** `item`+`field` (la valeur du coffre *est* le contenu) **soit**
`template:` (un modèle local est rendu). Les deux ensemble sont un refus : il n'y a qu'un fichier à
produire.

Deux règles portent tout le risque de cette forme :

- **tout ou rien, par fichier.** Asymétrie assumée avec le presse-papier : là, un marqueur non
  résolu reste littéral parce qu'on le *voit* dans ce qu'on colle ; ici le fichier part sur le NAS
  sans être relu. Un seul marqueur manquant, et ce fichier-là n'est pas écrit ;
- **le chemin est contraint au dossier du compose** (`SecretTemplatePath`). C'est la seule annotation
  qui désigne *quoi lire*, et elle vient d'un fichier : sans garde, un `template: ../../../.ssh/id_rsa`
  ferait lire une clé privée et l'écrirait, rendue, dans `secrets/`. On compare les chemins
  **résolus**, jamais la chaîne — vérifié par mutation.

Les modèles sont lus **avant** d'ouvrir le coffre, et les fins de ligne d'un modèle sont normalisées
en LF. Une valeur du coffre, jamais : c'est un secret, on l'écrit telle qu'elle est.

### Les périmés : signalés, supprimés sur demande

Une clé disparue laisse son fichier **intact**. Le supprimer d'office ferait d'un coffre
temporairement inaccessible la cause d'un déploiement détruit. Le bouton **Supprimer ces fichiers**
demande un clic, et ne supprime **que** des noms issus d'annotations `x-bw` dont la clé manque —
jamais un balayage de `secrets/`, qui peut contenir autre chose. Les deux règles sont vérifiées par
mutation.

### L'échappement, et l'ordre qui le rend compatible

Un antislash devant les accolades — `\{{ bw:item:champ }}` — dit « montre ce marqueur, ne le
résous pas » : sans lui, un fichier qui **documente** la syntaxe passe pour un fichier à secrets.

Mais un marqueur échappé produit un `{{ … }}` littéral, que le second filet rejetterait. Les quatre
étapes de `Render` existent pour que les deux tiennent : trouver (hors échappés) → substituer →
**balayer** (hors échappés) → **puis seulement** retirer l'antislash. Le retirer plus tôt le ferait
refuser par la garde censée nous protéger.

Un `{{ … }}` **non** échappé est toujours signalé — un test le vérifie aux côtés d'un échappé,
parce que c'est précisément ce qu'un échappement mal placé masquerait sans bruit.

`SecretRenderResult` est *soit* un texte, *soit* une liste d'échecs ; lire `Text` sur un échec lève.
`Missing` vit **à côté** du texte : ce n'est pas un troisième état, c'est un succès qui sait ce qui
lui manque.

**Conséquence assumée du second filet** : il rejette *tout* `{{ … }}`, y compris un gabarit Go ou
Jinja légitime qui cohabiterait dans le fichier. Il ne sait pas distinguer, et il se trompe du bon
côté.

## Créer ce qui manque

Entre le déverrouillage et le rendu, un item absent ou un champ vide sur un item existant peut être
**créé**, dans un formulaire unique — un champ de saisie par champ manquant, groupé par item. Le
formulaire n'apparaît que s'il y a quelque chose à proposer ; sinon on rend directement, comme
avant.

`SecretVault.Classify` range chaque marqueur dans l'un de quatre cas :

- **trouvé** — rien à proposer ;
- **item absent** → créable, item **Identifiant** neuf ;
- **champ absent ou vide** sur un item unique → complétable, **en place** ;
- **item en double** → **non créable**, reste une erreur affichée comme avant : écrire dans l'un des
  deux au hasard serait pire que de ne rien proposer.

**Placement des champs** : `password`, `username`, `notes`, `totp` vont dans les champs standards de
l'item ; tout autre nom devient un champ personnalisé **masqué** (type hidden) — jamais en clair,
et jamais recopié dans un champ standard qu'il ne visait pas.

**La relecture prouve l'écriture.** Après `create item` ou `get item` + `edit item`, un nouveau
`list items` alimente le rendu : rendre avec les valeurs saisies ferait croire à un succès même si
le coffre avait refusé. Un refus n'arrête pas les autres items — chaque écriture est séparée, un
échec rejoint la liste des manques, même règle qu'un `sync` qui échoue.

**Le piège de `bw edit item`** : il remplace la fiche **entière**. `BwItemPatch` travaille donc sur
la sortie **complète** de `bw get item`, en JSON, et ne touche qu'aux champs visés — sinon les URLs,
pièces jointes et l'historique d'un item existant disparaîtraient derrière un modèle volontairement
partiel.

**Charge utile par stdin.** Le JSON de la fiche — donc les valeurs saisies — est encodé en base64 et
passé par l'**entrée standard** de `bw create item` / `bw edit item`, jamais en argument : une ligne
de commande est lisible de tout processus de la machine, y compris par la lecture WMI que DockPad
fait lui-même pour `SwitchToProcess`. Les arguments se limitent à `create item`, `get item <id>`,
`edit item <id>` et `--organizationid <id>`.

**Aucune clé de session ne survit à une injection.** Le formulaire s'intercale entre la lecture et
l'écriture, donc la clé doit survivre à la saisie : elle vit dans la fermeture du `SecretWriter`
rendu par `OpenAsync`, jamais dans un champ de la fenêtre ni du service, et disparaît à
`InjectionSession.Close()` — à la fin de l'injection, ou à la fermeture de la fenêtre.

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
                          └─ SecretInjectionWindow    vérification → déverrouillage → [création] → travail → compte-rendu
                               ├─ SecretInjectionService   status · unlock · organizations · items · collections · create/edit item
                               │    ├─ BitwardenCli        le seul point qui lance bw.exe
                               │    ├─ SecretVault         PUR — un item, aucun, ou deux — et Classify, les quatre cas
                               │    │    └─ SecretFieldResolver   PUR — l'ordre des champs
                               │    ├─ SecretCreationPlan  PUR — ce que le formulaire propose, groupé par item
                               │    ├─ BwItemPatch         PUR — la fiche envoyée à create/edit, sans rien perdre
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
bw config server https://vaultwarden.<ton-tailnet>.ts.net   # ou l'URL de ton instance
bw login                                                      # mot de passe maître + TOTP
```

**Par winget, pas par npm** : winget prend le binaire des releases GitHub officielles de Bitwarden
et en vérifie l'empreinte, là où une installation npm expose en plus à toute la chaîne de
dépendances transitives. Le client de bureau Bitwarden ne fournit **pas** la CLI : ce sont deux
produits distincts.

Vaultwarden n'ayant qu'un coffre par compte, la séparation se fait par **organisation** : une
organisation dédiée évite qu'un item personnel du même nom rende la résolution ambiguë. Elle se
règle dans ☰ → Paramètres → Options.
