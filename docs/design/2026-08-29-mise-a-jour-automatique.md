# Mise à jour automatique — conception

**État** : proposition, non implémentée. **Variante retenue** : dossiers versionnés + jonction de
répertoire.

## Ce qu'on veut

DockPad détecte qu'une version plus récente est publiée, l'annonce discrètement, et l'installe d'un
clic — puis se relance dessus. Le modèle Chrome : on ne coupe jamais l'utilisateur dans son travail,
on lui signale et il choisit quand.

## Le problème central, et pourquoi cette variante

Windows verrouille les fichiers d'un programme qui tourne. On s'y est heurté une dizaine de fois en
développant : `MSB3021`, « le fichier est verrouillé par DockPad ». Et pas seulement par l'instance
principale — **les relais `--mcp` et `--url` verrouillent les DLL** sans être elle.

Une mise à jour qui écrase les fichiers en place doit donc fermer *tous* ces processus, et une copie
interrompue laisse une installation cassée. La variante retenue **supprime le problème au lieu de le
contourner** : la nouvelle version va dans un dossier neuf, rien n'est jamais écrasé.

## Disposition des fichiers

```
C:\DockPad\
  current  ──►  app\1.13.0          jonction de répertoire
  app\
    1.13.0\    DockPad.exe, DockPad.dll, fr\, qps-Ploc\, …
    1.14.0\    version suivante, une fois téléchargée
  update\      zone de travail (téléchargement, extraction)
```

- **Les chemins enregistrés** deviennent `C:\DockPad\current\DockPad.exe` — stables pour toujours
- **Le profil ne bouge pas** : `%APPDATA%\DockPad\` (configs, icônes, logs, sauvegardes) est en
  dehors de tout ça. Une mise à jour ne peut pas y toucher, c'est ce qui rend l'opération sûre
- **L'artefact de release ne change pas.** Le zip publié est plat (`DockPad.exe` à sa racine) : il
  s'extrait tel quel dans `app\<version>\`. Les releases 1.12.0 et 1.13.0 déjà en ligne seraient donc
  utilisables comme cibles sans être republiées

### La bascule

```powershell
Remove-Item  C:\DockPad\current
New-Item -ItemType Junction -Path C:\DockPad\current -Target C:\DockPad\app\1.14.0
```

**Vérifié sans droits admin** : une jonction de répertoire se crée et se re-pointe sans élévation,
contrairement à un lien symbolique. `C:\DockPad` est lui-même écrivable sans admin sur cette machine
— **aucune UAC n'est nécessaire**, ce qui est la condition pour que l'expérience soit « agréable ».

### Le point faible, dit franchement

Entre la suppression et la recréation, `current` n'existe pas pendant quelques millisecondes. Si
Windows lance DockPad pile à cet instant — un clic sur une URL — le lancement échoue. On ne peut pas
l'éliminer : renommer un répertoire par-dessus un autre n'est pas atomique sous Windows. On le réduit
en enchaînant les deux opérations sans rien faire entre elles, et on l'accepte : au pire un clic
d'URL est perdu et l'utilisateur reclique.

> L'alternative — un exe lanceur lisant un fichier pointeur, remplacé atomiquement — n'a pas ce
> défaut, mais impose un processus parasite derrière **chaque** lancement, y compris derrière chacun
> des relais `--mcp`, dont le stdio doit alors être relayé. Le remède est pire que le mal ici.

## Détection

- **Source** : `https://api.github.com/repos/syl-craft/DockPad/releases/latest`. Dépôt public, donc
  **aucun jeton**. L'API donne le tag, la date, et l'URL directe du zip — vérifié
- **Comparaison** : `System.Version` sur le tag (`v1.14.0`) contre `AppInfo`. Jamais une comparaison
  de chaînes : `1.9.0` est plus récent que `1.10.0` en ordre lexicographique
- **Cadence** : au démarrage, puis toutes les six heures tant que l'application tourne. L'horodatage
  du dernier appel est persisté dans `settings.json`, sinon redémarrer souvent martèlerait l'API —
  c'est exactement l'erreur qu'on a déjà commise avec le quota Claude, qui s'était fait renvoyer un
  HTTP 429 par DockPad lui-même
- **Un échec est silencieux** : hors ligne, proxy, API changée, quota dépassé → rien à l'écran, une
  ligne au journal. Ce chemin ne doit jamais empêcher d'utiliser l'application

## Ce que voit l'utilisateur

Une pastille discrète dans la toolbar, à côté du ☰, et une entrée dans le menu sous *Configuration* :

```
⬆ 1.14.0 disponible
```

Au clic, une fenêtre reprenant le patron des autres fenêtres de config : le numéro de version, les
notes de la release, et deux boutons — **Installer et relancer** / **Plus tard**. Pendant le
téléchargement, une barre de progression ; l'application reste utilisable.

**Ce que je n'ai pas décidé, et qui t'appartient** : faut-il aussi une mise à jour *silencieuse*,
téléchargée et posée sans rien demander, qui s'appliquerait au prochain démarrage — le vrai
comportement de Chrome ? C'est plus agréable encore, mais ça installe du code sans clic.

## Installation, étape par étape

1. Télécharger le zip dans `update\DockPad-1.14.0.zip`
2. **Vérifier l'intégrité** avant d'extraire (voir plus bas)
3. Extraire dans `update\1.14.0\`
4. **Valider** : `DockPad.exe` présent, version du fichier conforme au tag attendu, satellites `fr` et
   `qps-Ploc` présents. Une archive tronquée ne doit jamais devenir la version courante
5. Déplacer `update\1.14.0\` vers `app\1.14.0\` (même volume, donc renommage instantané)
6. Basculer la jonction
7. Relancer : démarrer `current\DockPad.exe`, puis quitter

Les étapes 1 à 5 se font **pendant que l'application tourne**, sans rien verrouiller. Seules 6 et 7
sont sensibles, et elles durent quelques millisecondes.

### La relance

L'instance en cours doit quitter avant que la nouvelle prenne le mutex d'instance unique. Le plus
simple est que la nouvelle instance **attende** : elle démarre, ne prend pas le mutex, réessaie
pendant quelques secondes, puis s'installe. Sans cette attente, la relance échoue silencieusement —
on l'a vu plusieurs fois aujourd'hui, une instance qui n'obtient pas le mutex se termine avec le code
0, sans un mot.

**Les relais `--mcp` déjà lancés continuent de tourner sur l'ancien dossier.** Ils parlent à
l'instance principale par un tuyau nommé, donc ils continuent de fonctionner — mais ils gardent des
poignées sur `app\1.13.0\`, ce qui **empêche son ménage**. Conséquence : le nettoyage doit tolérer un
dossier verrouillé et réessayer plus tard, jamais échouer.

## Intégrité de ce qu'on télécharge

C'est le point qui mérite le plus d'attention : une mise à jour automatique est, par construction, un
mécanisme qui **exécute du code téléchargé**.

- **HTTPS et une seule origine** : `github.com` / `api.github.com`, en dur. Jamais une URL lue dans
  une réponse sans vérifier qu'elle pointe bien là
- **Empreinte publiée** : le profil de publication doit produire un `DockPad-x.y.z.zip.sha256` à côté
  du zip, attaché à la release. L'API GitHub n'expose pas de somme de contrôle pour un asset ; sans
  ce fichier, on ne peut vérifier que la taille, ce qui ne détecte qu'une troncature
- **L'application n'est pas signée aujourd'hui.** Une signature Authenticode serait la vraie réponse
  — on vérifierait la signature du nouvel exe avant de basculer. C'est un coût annuel (certificat) et
  une décision qui t'appartient. Sans elle, la sécurité repose entièrement sur HTTPS et sur le fait
  que le compte GitHub n'est pas compromis

## Ce qui peut échouer, et ce qu'on fait

| Situation | Comportement |
|---|---|
| Hors ligne, DNS, proxy | Silence, une ligne au journal. Réessai au prochain cycle |
| API GitHub indisponible ou changée | Idem. Jamais d'erreur à l'écran |
| Téléchargement interrompu | Le fichier partiel est supprimé, rien n'est installé |
| Empreinte non conforme | Abandon, journalisé en `Warn`, la version est ignorée |
| Archive extraite incomplète | Détectée à l'étape 4, `update\` est nettoyé |
| Disque plein | Échec avant la bascule : l'installation en place reste intacte |
| La nouvelle version ne démarre pas | **Retour arrière** : re-pointer la jonction sur la version précédente |
| Un relais verrouille l'ancien dossier | Le ménage est reporté, ce n'est pas une erreur |

Le principe qui gouverne le tout : **rien n'est irréversible avant la bascule**, et la bascule elle-même
se défait en une commande.

## Retour arrière

`app\` garde la version précédente. Une entrée dans les Options — ou un argument `--rollback` —
re-pointe la jonction dessus. Le ménage ne supprime une version que lorsqu'une **plus récente qu'elle**
a démarré avec succès au moins une fois, ce qui garantit qu'il reste toujours un cran en arrière.

## Migration depuis la disposition actuelle

Trois chemins absolus pointent aujourd'hui sur `C:\DockPad\DockPad.exe` et doivent pointer sur
`C:\DockPad\current\DockPad.exe` :

1. `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — démarrage automatique
2. `HKCU\Software\Classes\DockPadURL\shell\open\command` — interception d'URL
3. L'enregistrement MCP (`claude mcp add dockpad -- "<chemin>" --mcp`), qui n'est **pas** dans le
   registre mais dans la configuration de Claude — l'application ne peut que l'annoncer, pas le
   corriger. La fenêtre MCP affiche déjà la commande à copier ; elle devra afficher la nouvelle

La migration a lieu une fois, au premier démarrage de la nouvelle disposition, et doit être idempotente.

## Ce que ça change dans le pipeline de release

- Le profil de publication produit **en plus** un `.sha256` du zip
- Le premier déploiement de cette disposition se fait à la main, comme aujourd'hui : vider
  `C:\DockPad`, créer `app\<version>\`, extraire, créer la jonction. Les suivants sont automatiques
- La release GitHub ne change pas de forme

## Découpage proposé

1. **Détection seule** — service de vérification, comparaison de versions, pastille et fenêtre
   d'annonce, clic qui ouvre la page GitHub. Utile tout de suite, sans aucun risque
2. **Disposition versionnée** — jonction, migration des trois chemins, publication du `.sha256`
3. **Installation** — téléchargement, vérification, extraction, bascule, relance, retour arrière
4. **Ménage** — suppression des vieilles versions, tolérante aux dossiers verrouillés

Les étapes 1 et 2 sont indépendantes et livrables séparément. L'étape 3 n'a de sens qu'après les deux.

## Ce qui se teste, et ce qui ne se teste pas

**Sans réseau ni machine réelle** — décisions pures, dans l'esprit du reste du dépôt :
comparaison de versions (`1.9.0` < `1.10.0`), décision « faut-il proposer la mise à jour »,
analyse de la réponse de l'API (avec un `HttpMessageHandler` factice, comme `ClaudeLimitsClient`),
validation d'une extraction, choix de la version à garder au ménage.

**Ce qui ne se teste qu'à la main** : la bascule de jonction, la relance, et le comportement quand un
relais verrouille l'ancien dossier. Ces trois-là devront être vérifiés sur une vraie installation, et
c'est là que se cachent les surprises.

## Questions ouvertes

1. **Mise à jour silencieuse** en plus du clic, appliquée au prochain démarrage ?
2. **Signature Authenticode** — on l'ajoute, ou on assume HTTPS seul ?
3. **Les versions préliminaires** — on ne suit que les releases stables, ou aussi les pré-releases ?
4. **Un canal de désactivation** : réglage « ne pas vérifier les mises à jour », pour un poste
   d'entreprise ?
