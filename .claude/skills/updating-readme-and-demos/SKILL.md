---
name: updating-readme-and-demos
description: Use when adding or changing a DockPad feature that the README shows, writing or re-rendering a motion design clip in tools/MotionDemo, editing README.md or README.fr.md, or publishing GIF/MP4 files to the DockPad-media repository
---

# README et motion designs de DockPad

## Vue d'ensemble

Le README existe en deux langues qui doivent rester **parallèles** : `README.md` en anglais,
`README.fr.md` en français. Les démonstrations sont des clips HTML de `tools/MotionDemo`, rendus en
GIF/MP4 **dans un autre dépôt**, `syl-craft/DockPad-media`, cloné à côté (`C:\dev\DockPad-media`).
La section « Motion designs de démonstration » de `CLAUDE.md` décrit le moteur. Ce skill donne le
déroulé et les contrôles.

## Déroulé d'un nouveau clip

1. **Branche** depuis `master` — ou depuis une branche qui touche déjà les README et n'est pas
   fusionnée, sinon les deux se contrediront ; dans ce cas, demander laquelle. Jamais de bump de
   version (`CLAUDE.md`, Versioning). Identité de commit vérifiée dans **les deux** dépôts
   (skill `deploy-artefact`).
2. **Le clip existe-t-il déjà ?** Lire la liste de `CLAUDE.md` : si un clip montre déjà le geste
   (`06-composite` montre 🔒 → ✓), proposer de l'étendre plutôt que d'en créer un second.
3. **Libellés** : relever dans `DockPad.Core/Resources/Strings*.resx` **tout texte que l'application
   affiche** — boutons, menus, infobulles, messages — 1337 compris (`Strings.qps-Ploc.resx`). Seules
   les légendes du clip, en anglais, sont libres de forme.
4. **Clip** `clips/NN-nom.html`, numéroté à la suite, calqué sur le clip le plus proche.
   Contraintes, toutes vérifiées par l'étape 6 :
   - 10 s au plus, `render(t)` sans état gardé d'une image à l'autre ;
   - la dernière image rejoint la première : fondu de sortie, et tout état global changé en cours de
     clip (thème, langue) défait avant la fin.
5. **Mise au point par images clés** : `node render.mjs --clip NN-nom --theme light --stills 1.5,3,6`
   puis lire chaque PNG avec Read, dans les deux thèmes.
6. **Rendu complet en arrière-plan** (~2 min 30 par clip et par thème) :
   `node render.mjs --clip NN-nom` avec `run_in_background`. Puis
   `bash .claude/skills/updating-readme-and-demos/scripts/verify-clips.sh NN-nom` et lire les
   planches qu'il produit.
7. **README** : voir ci-dessous, puis `bash .claude/skills/updating-readme-and-demos/scripts/check-readmes.sh`.
8. **Tableau** du `README.md` de DockPad-media, et la liste des clips dans `CLAUDE.md`.
9. **Commits** dans les deux dépôts, sans pousser. Montrer les vidéos à Sylvain.

## Intégrer un clip au README

Le même bloc dans les deux fichiers, sous le paragraphe d'introduction de la section :

```html
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/syl-craft/DockPad-media/main/videos/NN-nom-dark.gif">
  <img src="https://raw.githubusercontent.com/syl-craft/DockPad-media/main/videos/NN-nom-light.gif" alt="Demo: …" width="960">
</picture>
```

- **Le texte alternatif vaut pour les deux thèmes** : un clip rendu en sombre peut basculer vers le
  clair. « switching the theme », jamais « switching to dark »
- **Retirer les captures fixes que le clip rend redondantes**, dans les deux README, et réécrire la
  phrase qui les annonçait (« … each gets a tab: »). Garder celles qui montrent ce que le clip ne
  montre pas. Les fichiers restent dans `docs/screenshots/` : les notes de release les citent
- **Une fonctionnalité sans section en reçoit une**, dans les deux langues, au même rang, à côté de
  la section la plus proche par le sujet — `check-readmes.sh` compare les décomptes, pas l'ordre
- **Le README cite l'interface avec les libellés exacts du `.resx`** de sa langue

## Publier — seulement sur demande explicite

L'ordre compte : un README poussé avant ses GIF s'affiche cassé.

Fusionner dans `master` sans pousser DockPad-media laisse le README cassé dès le push suivant :
les deux publications vont ensemble.

1. `git -C ../DockPad-media push origin main`
2. Fusion `--no-ff` dans `master` (`merge: …`, convention du dépôt), puis `git push origin master`
3. `bash .claude/skills/updating-readme-and-demos/scripts/check-readmes.sh --online`

## Pièges rencontrés

| Symptôme | Cause | Parade |
|---|---|---|
| Liste déroulante ou menu placé au mauvais endroit | positions mesurées au build, avant que les libellés existent | poser de vrais textes avant de mesurer, ou mesurer dans `render` |
| Le glyphe qui change d'état est invisible | le curseur est posé dessus | viser quelques pixels à côté |
| Page blanche, rien ne s'affiche | apostrophe (« can't ») dans une chaîne JS entre `'…'` | guillemets doubles |
| La tuile zoomée colle au bord de l'image | zoom autour du point d'action | `setCamera(z, fx, fy, tx, ty)` : le point vient en (tx, ty) |
| Un libellé du clip ne correspond pas à l'app | texte écrit de mémoire | toujours depuis le `.resx` (deux clips ont dû être rendus à nouveau) |
| Gros binaires dans DockPad | rendu commité au mauvais endroit | `render.mjs` écrit dans DockPad-media ; jamais de GIF/MP4 dans ce dépôt |
