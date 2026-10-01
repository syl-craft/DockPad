#!/usr/bin/env bash
# Controle les deux README de DockPad.
#
#   bash check-readmes.sh           parite EN/FR + liens locaux + GIF presents dans le depot media local
#   bash check-readmes.sh --online  en plus : chaque URL de GIF repond 200, et le README rendu par
#                                   GitHub contient autant de <picture> que le fichier (apres push)
#
# Code 1 au premier ecart : les deux README doivent rester paralleles, un GIF cite doit exister.
set -u
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
media="${DOCKPAD_MEDIA_DIR:-$repo/../DockPad-media/videos}"
cd "$repo" || exit 1
fail=0

count() { grep -cE "$1" "$2"; }
printf '%-14s %8s %8s\n' '' README.md README.fr.md
for k in titres:'^#' clips:'<picture>' images:'!\[' tableaux:'^\|'; do
  name=${k%%:*}; rx=${k#*:}
  a=$(count "$rx" README.md); b=$(count "$rx" README.fr.md)
  printf '%-14s %8s %8s %s\n' "$name" "$a" "$b" "$([ "$a" = "$b" ] || echo '  <- ECART')"
  [ "$a" = "$b" ] || fail=1
done

# Liens locaux (captures, CHANGELOG, LICENSE…) : le fichier doit exister.
grep -ohE '\]\((docs|Assets|LICENSE|CHANGELOG|README)[^)#]*' README.md README.fr.md | sed 's/](//' | sort -u |
  while read -r f; do [ -e "$f" ] || { echo "lien local casse : $f"; exit 1; }; done || fail=1

# GIF cites : chacun doit exister dans le depot media, dans les deux themes.
urls=$(grep -ohE 'https://raw.githubusercontent.com/syl-craft/DockPad-media/main/videos/[a-z0-9-]+\.gif' README.md README.fr.md | sort -u)
for u in $urls; do
  [ -f "$media/${u##*/}" ] || { echo "GIF absent du depot media : ${u##*/}"; fail=1; }
done
for u in $(echo "$urls" | grep -- '-light\.gif$'); do
  echo "$urls" | grep -q "${u%-light.gif}-dark.gif" || { echo "version sombre non citee : ${u##*/}"; fail=1; }
done

if [ "${1:-}" = --online ]; then
  for u in $urls; do
    code=$(curl -s -o /dev/null -w '%{http_code}' "$u")
    [ "$code" = 200 ] || { echo "$code ${u##*/}  (le depot media est-il pousse ?)"; fail=1; }
  done
  gh=$(command -v gh || echo "/c/Program Files/GitHub CLI/gh")
  rendered=$("$gh" api repos/syl-craft/DockPad/readme -H "Accept: application/vnd.github.html" | grep -c '<picture>')
  [ "$rendered" = "$(count '<picture>' README.md)" ] || { echo "README rendu par GitHub : $rendered <picture>, fichier : $(count '<picture>' README.md) (master est-il pousse ?)"; fail=1; }
  echo "en ligne : $(echo "$urls" | wc -w) GIF verifies, $rendered <picture> rendus"
fi
[ $fail = 0 ] && echo OK
exit $fail
