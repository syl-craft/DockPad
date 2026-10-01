#!/usr/bin/env bash
# Verifie les rendus d'un ou plusieurs clips dans le depot DockPad-media, puis construit une planche
# d'images cles a relire.
#
#   bash verify-clips.sh 07-mcp                  un clip, les deux themes
#   bash verify-clips.sh 07-mcp 08-favorites     plusieurs
#   bash verify-clips.sh                          tous
#
# Echec (code 1) si une video n'est pas en 1920x1080, 60 i/s, 10 s au plus, ou si sa boucle saute :
# premiere et derniere image comparees en PSNR, sous 45 dB le raccord se voit dans le GIF.
set -u
media="${DOCKPAD_MEDIA_DIR:-$(cd "$(dirname "$0")/../../../.." && pwd)/../DockPad-media/videos}"
out="${TMPDIR:-${TEMP:-/tmp}}/dockpad-verify-clips"
mkdir -p "$out"
cd "$media" || { echo "depot media introuvable : $media"; exit 1; }

# ffprobe sous Windows termine ses lignes par CR : sans ce nettoyage, « 60/1 » ne vaut jamais « 60/1 ».
probe() { ffprobe -v error "$@" -of csv=p=0 | tr -d '\r'; }

clips=("$@")
[ ${#clips[@]} -eq 0 ] && clips=($(ls *-light.mp4 | sed 's/-light\.mp4$//'))

fail=0
printf '%-28s %-10s %-6s %-6s %-6s %-6s %s\n' video taille fps duree mp4 gif "boucle (dB)"
for c in "${clips[@]}"; do
  for th in light dark; do
    n="$c-$th"
    [ -f "$n.mp4" ] || { echo "MANQUANT $n.mp4"; fail=1; continue; }
    [ -f "$n.gif" ] || { echo "MANQUANT $n.gif"; fail=1; }
    IFS=, read -r w h fps < <(probe -select_streams v:0 -show_entries stream=width,height,r_frame_rate "$n.mp4")
    dur=$(probe -show_entries format=duration "$n.mp4")
    ffmpeg -v error -y -i "$n.mp4" -vf "select=eq(n\,0)" -frames:v 1 "$out/$n-first.png"
    ffmpeg -v error -y -sseof -0.05 -i "$n.mp4" -frames:v 1 "$out/$n-last.png"
    psnr=$(ffmpeg -i "$out/$n-first.png" -i "$out/$n-last.png" -lavfi psnr -f null - 2>&1 | grep -o 'average:[^ ]*' | cut -d: -f2 | tr -d '\r')
    printf '%-28s %-10s %-6s %-6s %-6s %-6s %s\n' "$n" "${w}x${h}" "$fps" "${dur:0:5}" "$(du -h "$n.mp4" | cut -f1)" "$(du -h "$n.gif" 2>/dev/null | cut -f1)" "$psnr"
    [ "$w" = 1920 ] && [ "$h" = 1080 ] && [ "$fps" = 60/1 ] || { echo "  ^ format inattendu"; fail=1; }
    awk "BEGIN{exit !($dur <= 10.001)}" || { echo "  ^ plus de 10 s"; fail=1; }
    [ "$psnr" = inf ] || awk "BEGIN{exit !($psnr >= 45)}" || { echo "  ^ la boucle saute"; fail=1; }
    # Planche : quatre images reparties sur le clip, a relire avec Read.
    ffmpeg -v error -y -i "$n.mp4" -vf "select='eq(n\,90)+eq(n\,240)+eq(n\,390)+eq(n\,510)',scale=480:-1,tile=4x1" -frames:v 1 -fps_mode vfr "$out/$n-sheet.png"
  done
done
echo
echo "Planches a relire : $out/<clip>-<theme>-sheet.png"
exit $fail
