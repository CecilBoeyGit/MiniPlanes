# Sound sources

The game's sounds in `Assets/MiniPlanes/Audio` are cut from the Skylooper sound collection
(`D:/Unity/MiniPlanes_Docs/SkylooperSounds/Sounds`) by `convert_sfx.sh`. Each was trimmed of leading
silence, cut short, faded out, made mono and peak-normalised. Most come from freesound.org, and the number
at the start of each source name is its freesound ID (`https://freesound.org/s/<id>/`). Check each sound's
licence there before release: some freesound licences need attribution, and some don't allow commercial use.

| Game sound | Source |
|---|---|
| ring_pass | synthesized (make_sfx.py) |
| ring_big_o | synthesized (make_sfx.py) |
| ring_clip | Hand/Ring Error/869696 ps1 pick up sfx 9 |
| plane_hit | Hit/Thud.wav |
| plane_crash | Death/849842 explosion |
| engine_loop | Propeller/607776 75 Hz flying plane sound loop |
| boost | Propeller/347973 propeller plane flyby |
| flick | Hand/Flick Enemy/410364 metal hit |
| gun_burst | Enemy Plane Gunner/Bullet Machine Gun/Bullet Sray.wav |
| bullet_block | Hit/30932 bullet ricochet |
| turret_on | Random/733121 item acquired |
| turret_shot | Death/417979 sonic shoot |
| shield_on | Shield Active/249818 magic shield |
| shield_break | Shield Active/558972 shield hit 1 |
| balloon_pop | PowerUp/186719 chime 01 (played when a powerup is picked up) |
| bomb_armed | Enemy Plane Bomber/Lock On/33732 alarm short |
| explosion | Death/516914 explosion medium blast |
| small_blast | Death/584119 small explode blast |
| defused | Hand/Flick Enemy/729216 upgrade select ui |
| tube_break | Hand/Ring Error/67617 metal crash collision |
| island_shatter | Death/847085 metal crash with glass debris |
| dust | Hand/Flick Enemy/207499 spoon hits trash can |
| boss_alarm | Enemy Plane Bomber/Lock On/324394 invasion alarm |
| missile_lock | Enemy Plane Bomber/Lock On/106314 target acquired |
| missile_launch | Enemy Plane Bomber/Missile Launched/854476 m142 himars rocket launch |
| ui_click, unpause | UI/Pause/167126 unpause |
| pause | UI/Pause/167127 pause |
| title | UI/Title Screen/858479 friendly ui app jingle |
| level_complete | UI/Level Complete l New Wave/790472 level completed |
| new_wave | UI/Level Complete l New Wave/New Wave 2.wav |
| level_failed | UI/Lose Screen/751863 failure notification sound |
| purchase | UI/Level Complete l New Wave/656395 upgrade |
| reward | Random/854025 fantasy ui stinger magical level up |
| enemy_explode | Death/584119 small explode blast, layered with a synthesized boom and debris (make_sfx.py) |
| enemy_engine_jet, enemy_engine_prop | synthesized (make_sfx.py) |
| streak_reveal, enemy_warning, life_up | synthesized (make_sfx.py) |

## Overlaps

`Sfx` plays one sound per action: a pause, resume or purchase button plays its own sound without the click; a
clipped ring that hurts the plane plays only the hit; the three-in-a-row reveal replaces that ring's chime; a new
best replaces the game-over jingle. Jingles wait for what they would talk over (game over waits for the crash,
the next-wave and level jingles wait for the finish hit) and the music dips under them, staying silent under the
title jingle. The rules are the `Replaces` and `WaitsFor` tables in `Sfx.cs`.

## Synthesized sounds and music

`Tools/Audio/synth` makes the rest from scratch with numpy (no samples, so no licence to check):

- `make_sfx.py`: the bubbly ring scores, the fighter jet and bomber propeller loops, the enemy explosion, and
  `Music/riser` and `Music/climax`.
- `make_music.py`: `bgm_menu` (other menus, 120 BPM B-flat major in the title's style), `bgm_start` (no longer
  used; the start page now plays the title loop) and `bgm_boss`. Melodies, chords and drum patterns are written in
  the script. Its synthesized `normal`, `hostile` and `uprise` versions are only made when named.

## Title theme and in-flight music (`make_title.py`)

Source: `UI/Title Screen/858479__tommasomotteran__friendly-ui-app-jingle.wav`, "Friendly UI App Jingle" by
TommasoMotteran, https://freesound.org/s/858479/, **CC0** (no attribution needed). Piano and kalimba, 47.4 s,
recorded on an exact 120 BPM grid in B-flat major; `title.ogg` is its first 4 s.

Steps (all in `make_title.py`):
1. Bars start at 0.266 s. Bars 0-1 are the opening; bars 2-17 are a 16-bar section that the recording repeats
   from bar 18 (bar 18 matches bar 2 best of all bar pairs; the repeat is 1,411,200 samples = 32.000 s later).
2. `Music/bgm_title_intro` = the opening, from the first sound to 0.25 s past bar 2's downbeat (4.38 s).
3. `Music/bgm_title` = the next 32.000 s. Its last 0.25 s blends, equal-power, from the recording's own repeat
   of bar 2 into bar 2 itself, so the loop end runs into its start with no click; the intro runs into the loop
   start because the cut points are consecutive samples of the recording. Both share one gain (loop at -16 LUFS).
4. `Music/bgm_normal`, `bgm_hostile`, `bgm_uprise` = the same 32 s loop plus synthesized drums (four-on-the-floor,
   claps, galloping hats, fills, crashes; harder and denser for hostile and the build) and layers made from the
   recording itself (a saturated bass doubling, a gated copy in the title's da-da-.-da gallop for hostile, a gated
   shimmer for the build), with a sidechain pump. Same melody, key, bars and length, so they crossfade in lock-step.
   The drums land within about 4 ms of the recording's notes.
5. The game plays the intro then the loop on the start page (`GameMusic.startMenu`, `startMenuIntro`). At boot the
   title cue starts it from the intro instead of playing `title.ogg` as a separate sound, so the opening isn't doubled.
   `Music/climax` (the finish hit) is in B-flat to match.

Run with Blender's Python (numpy is all they need):
`"/c/Program Files/Blender Foundation/Blender 5.2/"*/python/bin/python.exe -I Tools/Audio/synth/make_title.py`
and `.../make_music.py [menu start boss]`, `.../make_sfx.py [names]`.
Open `Tools/Audio/listen.html` in a browser to hear them and try the in-flight crossfades.
