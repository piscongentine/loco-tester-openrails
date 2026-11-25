LOCO TESTER  1.1 "Power Up"
Made by Piscongentine · Made in Bharat

A workbench for Open Rails / MSTS locomotives.

START
  Double-click "Loco Tester" on the desktop. It opens C:\OR by default; use Browse… to pick another
  Open Rails / MSTS folder, its TRAINS folder or TRAINS\TRAINSET.
  Locos are listed under Electric / Diesel / Steam. Pick one, then press Power (O) to start its engine.

PAGES
  Overview   key numbers, the files the loco uses, and a health check of its sounds, shape and cab
  3D View    the real model with textures: drag to rotate, right-drag to pan, wheel to zoom,
             Front / Side / Rear / Top / ¾ cameras, Cab POV (driver's eye), Night, Turntable
  Cab        the 2D cab with day, night and cab-light pictures; click horn, bell, sander, wipers,
             pantograph, brake, throttle and other controls right on the cab
  Sound Lab  every sound stream live, every trigger in the .sms as a button, sound input sliders,
             and an event log
  About      version, features, keyboard keys

POWER
  A loco starts with its engine off. Power starts it (the starter cranks, then it idles);
  Stop or Esc stops the loco and runs the engine down. With the power off, every other control
  shows "Power is off".

TEST DECK (bottom)
  Horn, Bell, Pantograph 1/2, Lights Off/Dim/Bright, Cab light, Wipers, Sander, Compressor,
  Brake, Doors, Power, Alerter, Radio, a speed and a throttle slider, and "Run 0 → 100"
  (speeds up to 100 km/h or the loco's top speed, holds, then brakes to a stop).
  Horn, bell and sander sound while held and play their ending when released, like MSTS.
  Faded buttons = this loco's sound file has no sound for that test.
  "No cab light" (greyed) = this cab has no lit-cab (CABLIGHT) pictures.

REPLACE SOUNDS
  "Replace sounds" (top right) lists the loco's horn, bell and sander. ▶ plays the current sound,
  Replace… takes your own .wav (PCM WAV), ↺ Original puts the original back.
  The new .wav is copied next to the .sms and the untouched .sms is kept as <name>.sms.LocoTester_original.

KEYS
  O power   H horn (hold)  B bell (hold)  S sander (hold)  Space brake (hold)  A alerter
  P / Shift+P pantograph 1 / 2   L headlights   C cab light   W wipers
  K compressor  D doors  R run 0→100  Esc stop the loco and the engine
  Up/Down throttle  PgUp/PgDn speed  1-5 pages  Ctrl+F search  F5 rescan

REPAIR SOUNDS
  "Repair sounds" first backs up the loco's .eng and .sms files to <loco folder>\LocoTester_Backup\<date_time>\,
  then fixes what it can: missing or empty .sms files (rebuilt from the closest healthy loco of the same type),
  missing Sound ( ) lines in the .eng, damaged or missing brackets, a missing SIMISA header, corrupt characters,
  wrong Streams / Triggers / CurvePoints counts, and sound files that are missing or misnamed.
  .wav files are never changed, only added. Shared sounds in the global SOUND folder are never edited.
  To undo a repair, copy the files from the backup folder back.

COMMAND LINE
  "Loco Tester.exe" --selftest [TRAINSET folder]   makes LT_DUMMY_* copies of a healthy loco, damages their
                                                   sound files 4 different ways, repairs them and reports
  "Loco Tester.exe" --soundtest <file.eng> [OR folder]   plays the loco's sounds through the mixer and reports
  "Loco Tester.exe" --cablights [OR folder]              lists which locos have a working cab light
  "Loco Tester.exe" --swap <file.eng> horn|bell|sander <file.wav>|restore
  Start options: [folder] --power --page=overview|3d|cab|sound|about --night --panto --lights=1|2
                 --speed=<km/h> --throttle=<%> --reverse --camera=front|side|rear|top|pov

UPDATE LOG
  What changed in every version, with a download link for each:
  https://github.com/piscongentine/loco-tester-openrails/blob/main/CHANGELOG.md

Needs Windows 10/11 with .NET Framework 4.8 (built in).
