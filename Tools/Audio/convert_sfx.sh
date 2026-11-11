#!/usr/bin/env bash
# Picks the game's sound effects from the Skylooper sound collection and converts them to short mono OGG
# files in Assets/MiniPlanes/Audio: leading silence removed, cut to length, faded out, peak-normalised.
#   bash Tools/Audio/convert_sfx.sh
set -euo pipefail
cd "$(dirname "$0")/../.."
SRC="D:/Unity/MiniPlanes_Docs/SkylooperSounds/Sounds"
OUT="Assets/MiniPlanes/Audio"
FF="${FFMPEG:-C:/Users/cecil/Tools/Faster-Whisper-XXL/ffmpeg.exe}"
mkdir -p "$OUT"

# name | source file | longest length in seconds (0 keeps it whole, for loops)
LIST=$(cat <<'EOF'
ring_clip|Hand/Ring Error/869696__signaturesoundsorg__ps1-pick-up-sfx-9.wav|0.4
plane_hit|Hit/Thud.wav|1.1
plane_crash|Death/849842__jeffpearson__explosion.mp3|2.1
flick|Hand/Flick Enemy/410364__benjaminnelan__metal-hit.wav|0.4
gun_burst|Enemy Plane Gunner/Bullet Machine Gun/Bullet Sray.wav|0.9
bullet_block|Hit/30932__aust_paul__bullet-ricochet.wav|0.8
turret_shot|Death/417979__anarchid__sonic_shoot.wav|0.3
turret_on|Random/733121__fuzzytuesday__item-aquired.wav|1.2
shield_on|Shield Active/249818__spookymodem__magic-shield.wav|1.5
shield_break|Shield Active/558972__fivebrosstopmosyt__shield-hit-1.wav|0.9
balloon_pop|PowerUp/186719__andromadax24__chime_01.wav|1.3
bomb_armed|Enemy Plane Bomber/Lock On/33732__jobro__1-alarm-short-e.wav|1.7
explosion|Death/516914__kinoton__explosion-medium-blast.wav|2.5
small_blast|Death/584119__robinhood76__10167-small-explode-blast.wav|1.5
defused|Hand/Flick Enemy/729216__techspiredminds__upgradeselect-ui (1).wav|0.45
boost|Propeller/347973__yurinikolai__propeller-plane-flyby.flac|2.0
tube_break|Hand/Ring Error/67617__qubodup__metal-crash-collision.flac|0.65
island_shatter|Death/847085__artninja__custom_metal_crash_with_glass_debris_03022026.wav|1.3
dust|Hand/Flick Enemy/207499__soundscape_leuphana__20131118_spoon-hits-trash-can2_zoomh2nxy.wav|0.25
boss_alarm|Enemy Plane Bomber/Lock On/324394__o_ultimo__003-invasion-alarm.mp3|3.0
missile_lock|Enemy Plane Bomber/Lock On/106314__timkahn__target-acquired.wav|2.3
missile_launch|Enemy Plane Bomber/Missile Launched/854476__qubodup__m142-himars-rocket-launch-8.wav|2.5
engine_loop|Propeller/607776__therandomsoundbyte2637__75-hz-flying-plane-sound-loop.flac|0
ui_click|UI/Pause/167126__crisstanza__unpause.mp3|0.5
pause|UI/Pause/167127__crisstanza__pause.mp3|0.5
unpause|UI/Pause/167126__crisstanza__unpause.mp3|0.5
title|UI/Title Screen/858479__tommasomotteran__friendly-ui-app-jingle.wav|4.0
level_complete|UI/Level Complete l New Wave/790472__organizedlaziness__level-completed.wav|2.6
new_wave|UI/Level Complete l New Wave/New Wave 2.wav|1.5
level_failed|UI/Lose Screen/751863__def__failure-notification-sound.wav|2.0
purchase|UI/Level Complete l New Wave/656395__nikos34__upgrade.wav|2.0
reward|Random/854025__tommasomotteran__fantasy-ui-stinger-magical-level-up-03.wav|2.5
EOF
)

echo "$LIST" | while IFS='|' read -r name src len; do
  [ -z "$name" ] && continue
  in="$SRC/$src"
  if [ "$len" = "0" ]; then
    trim="anull"
  else
    fade=$(awk -v l="$len" 'BEGIN { f = l * 0.25; if (f > 0.4) f = 0.4; printf "%.3f", f }')
    start=$(awk -v l="$len" -v f="$fade" 'BEGIN { printf "%.3f", l - f }')
    trim="silenceremove=start_periods=1:start_threshold=-50dB,atrim=0:$len,afade=t=out:st=$start:d=$fade"
  fi
  peak=$("$FF" -hide_banner -nostdin -i "$in" -af "aformat=channel_layouts=mono,$trim,volumedetect" -f null - 2>&1 | grep -o "max_volume: [-0-9.]*" | awk '{print $2}')
  gain=$(awk -v p="${peak:-0}" 'BEGIN { printf "%.2f", -1.0 - p }')
  "$FF" -hide_banner -nostdin -loglevel error -y -i "$in" \
    -af "aformat=channel_layouts=mono,$trim,volume=${gain}dB" -ar 44100 -ac 1 -c:a libvorbis -q:a 4 "$OUT/$name.ogg"
  printf "%-15s %6s dB  %s\n" "$name" "$gain" "$src"
done
