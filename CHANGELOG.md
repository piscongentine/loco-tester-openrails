# 📋 Update log

Every version of Loco Tester, newest first. Each one stays downloadable.

| Version | Released | Download | Release page |
|---|---|---|---|
| **1.1 "Power Up"** (latest) | 25 Nov 2026 | [LocoTester-Setup-1.1.exe](https://github.com/piscongentine/loco-tester-openrails/releases/download/v1.1/LocoTester-Setup-1.1.exe) | [v1.1](https://github.com/piscongentine/loco-tester-openrails/releases/tag/v1.1) |
| 1.0 "First Light" | 24 Nov 2026 | [LocoTester-Setup-1.0.exe](https://github.com/piscongentine/loco-tester-openrails/releases/download/v1.0/LocoTester-Setup-1.0.exe) | [v1.0](https://github.com/piscongentine/loco-tester-openrails/releases/tag/v1.0) |

Always the newest: **[LocoTester-Setup.exe](https://github.com/piscongentine/loco-tester-openrails/releases/latest/download/LocoTester-Setup.exe)**. Setup updates an installed copy in place; your settings are kept.

---

## 1.1 "Power Up" · 25 Nov 2026

[⬇️ Download 1.1](https://github.com/piscongentine/loco-tester-openrails/releases/download/v1.1/LocoTester-Setup-1.1.exe) · [Release page](https://github.com/piscongentine/loco-tester-openrails/releases/tag/v1.1)

### New
- **Engine power.** A loco now starts with its engine off. **Power** (key `O`) starts it: the starter cranks, the engine catches and settles to idle in about 4.5 s. **Stop** (or `Esc`) stops the loco and runs the engine down in about 3 s instead of cutting the sound. The loco's own power-on and circuit-breaker sounds play if its `.sms` has them.
- **Power first.** Horn, bell, sander, pantographs, lights, cab light, throttle, speed, run test, Sound Lab triggers and the clickable cab all check the power. With the power off, a "Power is off" message pops up instead.
- **Replace sounds.** A new **🔁 Replace sounds** window lets you swap a loco's horn, bell (or second horn) or sander for your own `.wav`:
  - preview the current sound, pick a new one, put the original back with **↺ Original**;
  - the new file is copied next to the loco's `.sms`, and the untouched `.sms` is kept as `<name>.sms.LocoTester_original`;
  - a `.wav` without loop points gets two, so it sustains while the button is held, like MSTS horns;
  - if the sound channel had a pitch curve tuned for the old file's sample rate (for example a fixed 11000 Hz for an 11 kHz whistle), it is scaled to the new file, so the new horn plays at its real pitch in Loco Tester, Open Rails and MSTS.
- **Cab light check.** Loco Tester looks for lit-cab (`CABLIGHT`) pictures in every cab view. Where they exist, Cab light turns the lit cab on and off; where they don't, the button is greyed out and shows "No cab light". Tested on all 106 locos of the test install: 21 with a cab light (AB / Amrit Bharat WAP-7, HWH and SRC WAP-7, WAP-5 30189), 16 without, 69 with no 2D cab.
- **Cleaner loco list.** Locos are grouped under **Electric / Diesel / Steam** headers with counts, one tidy row each: type badge, name, folder, top speed and a health dot, with a slim orange bar on the selected loco. Details are in the tooltip.
- Command line: `--power` (start with the engine running), `--cablights [folder]` (list which locos have a cab light), `--swap <eng> horn|bell|sander <file.wav>|restore`.

### Fixed
- **Engine sound stopping after a few seconds.** Looping sounds without cue points (for example the WDP4D's EMD idle and notch sounds) stopped after one pass, so the engine went silent as soon as you rotated the view or clicked anything. They now loop seamlessly.
- **Horn, bell and sander behave like MSTS / Open Rails.** The sound keeps going while the button (or key) is held and, when released, plays its ending part, or fades out smoothly if the file has none. Sander is now a hold control (`S` hold) like horn and bell.
- The Power button is never faded out, even when the loco's sound file has no power sound.
- Cab-light and night cab pictures are also found when the `.cvf` points into another folder.

---

## 1.0 "First Light" · 24 Nov 2026

[⬇️ Download 1.0](https://github.com/piscongentine/loco-tester-openrails/releases/download/v1.0/LocoTester-Setup-1.0.exe) · [Release page](https://github.com/piscongentine/loco-tester-openrails/releases/tag/v1.0)

First public version.
- Scans `TRAINS\TRAINSET` in a fraction of a second, with search and Electric / Diesel / Steam / Problems filters.
- Health check of every loco: missing or broken `.sms` and `.wav` files, brackets, counts, shapes and cab files.
- One-click sound repair with an automatic backup.
- HD 3D view of the real model on DB Tracks in a loco shed or on open track, with animated wheels, fans, rods, pantographs (with sparks), wipers, doors and mirrors.
- Lights from the `.eng` with a real night mode; diesel smoke and steam effects.
- 2D cab with day, night and cab-light pictures and clickable controls.
- Sound Lab that plays the `.sms` the way Open Rails does, with live meters and an event log.
- Run 0 → 100 km/h test and a "Check this PC" report.
- Inno Setup installer with a minimum-requirements page and a choice of install folder.
