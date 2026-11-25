<p align="center">
  <img src="docs/banner.svg" alt="Loco Tester – HD locomotive workbench for Open Rails and MSTS" width="100%">
</p>

<p align="center">
  <a href="https://github.com/piscongentine/loco-tester-openrails/releases/latest/download/LocoTester-Setup.exe"><img alt="Download for Windows" src="https://img.shields.io/badge/Download-Windows%20Setup-F7811E?style=for-the-badge&logo=windows&logoColor=white"></a>
  <a href="CHANGELOG.md"><img alt="Version" src="https://img.shields.io/badge/version-1.1%20Power%20Up-16233A?style=for-the-badge"></a>
  <img alt="Open Rails and MSTS" src="https://img.shields.io/badge/Open%20Rails%20%7C%20MSTS-ready-22C3B0?style=for-the-badge">
  <img alt="Made in Bharat" src="https://img.shields.io/badge/Made%20in-Bharat-FF9933?style=for-the-badge">
</p>

<p align="center"><b>Look at it. Listen to it. Drive it. Fix it.</b><br>
Point Loco Tester at your Open Rails or MSTS folder and every locomotive is ready to test in a second:<br>
an HD 3D model in a loco shed, its own lights, smoke and pantographs, the 2D cab, every sound in its <code>.sms</code>,<br>
a 0 → 100 km/h run, and a health check that repairs broken sound files for you.</p>

---

<p align="center">
  <img src="docs/img/demo-electric.gif" alt="Electric loco at night: pantograph up with sparks, bright headlights, wheels turning" width="49%">
  <img src="docs/img/demo-diesel.gif" alt="Diesel loco in the shed with exhaust smoke at high throttle" width="49%">
</p>

## 🆕 What's new in 1.1 "Power Up"

- ⏻ **Engine power**: press Power to start the engine (it cranks, then idles); Stop runs it down. Every control asks for power first.
- 📯 **Horn, bell and sander like MSTS**: they sustain while held and play their ending when released.
- 🔁 **Replace sounds**: put your own `.wav` on a loco's horn, bell or sander, with one-click restore of the original ([how](#-replace-a-horn-bell-or-sander-sound); `.wav` files only).
- 🔦 **Cab light check**: Cab light works on cabs with lit pictures and is greyed out on the rest.
- 🗂 **Cleaner loco list**, grouped by Electric / Diesel / Steam.
- 🐞 Fixed: diesel engine sounds (for example the WDP4D) going silent after a few seconds.

Full details and a download link for every version: **[Update log](CHANGELOG.md)**.

## ✨ Features

| | |
|---|---|
| 🚂 **Every loco in a second** | Scans `TRAINS\TRAINSET` in parallel (100+ locos in ~0.1 s), with search and Electric / Diesel / Steam / Problems filters. |
| 🩺 **Health check** | Finds missing or broken `.sms` files, missing `.wav` files, damaged brackets, wrong counts, missing shapes and cab files, for every loco in the background. |
| 🛠 **One-click sound repair** | Backs up first, then rebuilds missing or empty sound files from the closest healthy loco of the same type, fixes brackets, headers and counts, and finds renamed sounds. |
| 🧊 **HD 3D view** | The real `.s` model and `.ace` textures at full resolution, standing on the real **DB Tracks** track inside a loco shed, or on open track. Rotate, pan, zoom, preset cameras, turntable and a driver's-eye view. |
| ⚡ **Things that move** | Wheels, radiator fans, steam rods, pantographs (with sparks at the contact wire), wipers, doors and mirrors, all driven by speed and the test buttons. |
| 💡 **Real lights** | Lights from the `.eng`: dim and bright headlights at the end you drive toward, red tail lights, marker and blinking lights, emergency (penalty) lights. Glare, beams and a real night mode. |
| 💨 **Smoke and steam** | Diesel exhaust that thickens and darkens with the throttle; steam chimney, whistle and cylinder steam. |
| 🎛 **2D cab** | Day, night and cab-light pictures. Click the horn, bell, sander, wipers, pantograph, brake and more right on the cab. |
| 🔊 **Sound Lab** | Plays the `.sms` the way Open Rails does: cue-point loops, speed and engine-RPM curves, every trigger as a button, live stream meters and an event log. |
| 🏁 **Run 0 → 100** | Speeds up, cruises and brakes to a stop while you hear every sound stage. |
| ⏻ **Engine power** | Power starts the engine (cranking, then idle), Stop runs it down. Every control needs the power on, or a "Power is off" message pops up. |
| 🔁 **Replace horn / bell sounds** | Swap a loco's horn, bell or sander for your own sound (**`.wav` files only**): preview, replace, restore the original. Loop points and pitch are set up for you. [How it works](#-replace-a-horn-bell-or-sander-sound). |
| 🔦 **Cab light check** | Cab light turns the lit cab on and off where the cab has `CABLIGHT` pictures, and is greyed out where it does not. |
| 🧪 **Tests** | Horn, bell, pantographs, lights, wipers, sander, compressor, brake, emergency brake, dynamic brake, reverser, doors, mirrors, coupling, power, alerter, radio, plus injectors, blower, cylinder cocks, fire door and safety valve on steam locos. |
| 🔎 **Check this PC** | Requirements, graphics card and driver, sound, Open Rails folders and DB Tracks, in the setup and inside the app. |

## 📸 Screens

<p align="center">
  <img src="docs/img/3d-shed.png" alt="3D view: WAP-7 in the loco shed on DB Tracks" width="49%">
  <img src="docs/img/3d-night.png" alt="Night mode with bright headlights" width="49%">
  <img src="docs/img/cab.png" alt="2D cab with clickable controls" width="49%">
  <img src="docs/img/sound.png" alt="Sound Lab with live streams" width="49%">
  <img src="docs/img/overview.png" alt="Overview with health check" width="49%">
  <img src="docs/img/3d-diesel.png" alt="Diesel with exhaust smoke" width="49%">
</p>

## ⬇️ Install

1. Download **[LocoTester-Setup.exe](https://github.com/piscongentine/loco-tester-openrails/releases/latest/download/LocoTester-Setup.exe)** (about 3 MB). This link always gives the newest version (now 1.1). Older versions are in the [update log](CHANGELOG.md).
2. Run it. It installs for your Windows account and needs no administrator rights. Over an older version it simply updates it.
3. On the last page, tick **Check this PC** to see whether your graphics card, sound and Open Rails folder are ready.

> **"Windows protected your PC"?** Loco Tester is new and not code-signed yet, so SmartScreen does not know it. Click **More info → Run anyway**. The source code is all here if you want to check or build it yourself.

<p align="center"><img src="docs/img/setup.png" alt="Setup wizard" width="60%"></p>

### Requirements

| | Needed | Notes |
|---|---|---|
| Windows | 10 or 11 | 64-bit recommended |
| .NET Framework | 4.8 | Built into Windows 10 (1903+) and 11. Setup tells you if it is missing. |
| Graphics | DirectX hardware 3D | For the HD 3D preview. Keep the driver up to date. The **HD** button can be turned off on slow PCs. |
| Sound | any output | Everything else works without sound. |
| Content | Open Rails or MSTS folder with `TRAINS\TRAINSET` | Found automatically on first start. |
| Optional | [DB Tracks](https://www.trainsim.com/) in `GLOBAL\SHAPES` | Gives the real track under the loco; without it a simple track is drawn. |

## 🎮 Keys

| Key | Action | Key | Action |
|---|---|---|---|
| `O` | Power: start / stop the engine (first!) | `J` | Couple / uncouple |
| `H` (hold) | Horn | `B` (hold) | Bell |
| `S` (hold) | Sander | `K` | Compressor |
| `P` / `Shift+P` | Pantograph 1 / 2 | `L` | Headlights off / dim / bright |
| `C` | Cab light | `W` | Wipers |
| `Space` (hold) | Brake | `E` | Emergency brake |
| `Y` (hold) | Dynamic brake | `V` | Reverser |
| `D` / `M` | Doors / Mirrors | `R` | Run 0 → 100 |
| `Esc` | Stop the loco and the engine | | |
| `↑` `↓` | Throttle | `PgUp` `PgDn` | Speed |
| `1`–`5` | Pages | `Ctrl+F` / `F5` | Search / Rescan |

## 🔁 Replace a horn, bell or sander sound

Give any loco your own horn (or bell, second horn or sander) in a few clicks:

1. Pick the loco and click **🔁 Replace sounds** (top right).
2. Each sound has its own card: **▶** plays the current sound, **Replace…** lets you choose yours, **↺ Original** puts the original back.
3. Press **Power**, then hold **Horn** (`H`) to hear it: it sustains while held and plays its ending when you let go.

> ⚠️ **Use `.wav` files only.** Open Rails and MSTS play WAV sounds only, so Loco Tester accepts **PCM `.wav`** (8/16/24-bit, mono or stereo, any sample rate). MP3, OGG, M4A and other formats are refused. Convert them to WAV first (for example with Audacity: *File → Export → WAV, signed 16-bit PCM*).

What it does behind the scenes, so the sound also works in Open Rails and MSTS:

- copies your `.wav` next to the loco's `.sms` as `LT_horn_<name>.wav` and points every horn trigger at it;
- keeps the untouched sound file as `<name>.sms.LocoTester_original` (that's what **↺ Original** restores);
- adds two loop points to a `.wav` that has none, so it sustains while held, like MSTS horns;
- scales the channel's pitch curve when it was tuned to the old file's sample rate, so your horn plays at its real pitch.

## 🛠 Sound repair: what it changes

Before anything is touched, the loco's `.eng` and `.sms` files are copied to `<loco folder>\LocoTester_Backup\<date_time>\`. Then it can:

- recreate a missing or empty `.sms` from the closest healthy loco of the same type (with its sounds),
- add missing `Sound ( )` lines to the `.eng`,
- repair cut-off files and misplaced or extra brackets,
- restore the `SIMISA` header and remove corrupt characters,
- correct `Streams` / `Triggers` / `CurvePoints` counts,
- point renamed sound files at the right `.wav`.

`.wav` files are never changed, only added. Shared sounds in the global `SOUND` folder are never edited. To undo, copy the backup files back.

## 🧱 Build from source

Everything is plain C# 5 / WPF on .NET Framework 4.8, compiled with the C# compiler that ships with Windows. No Visual Studio or SDK needed.

```powershell
.\build.ps1          # builds build5\Loco Tester.exe
.\build-setup.ps1    # builds dist\LocoTester-Setup-<version>.exe and dist\LocoTester-Setup.exe (needs Inno Setup 6)
```

Self-tests and tools:

```powershell
& ".\build5\Loco Tester.exe" --selftest C:\OR\TRAINS\TRAINSET        # makes damaged LT_DUMMY_* copies, repairs and checks them
& ".\build5\Loco Tester.exe" --soundtest "C:\OR\TRAINS\TRAINSET\<loco>\<loco>.eng" C:\OR
& ".\build5\Loco Tester.exe" --cablights C:\OR                        # which locos have a working cab light
& ".\build5\Loco Tester.exe" --swap "<loco>.eng" horn "my horn.wav"    # same as the Replace sounds window ("restore" puts it back)
```

| Folder | What is in it |
|---|---|
| `src/` | The app: file readers (`Stf`, `Loco`, `Sms`, `Shape`, `TextShape`, `Cab`), sound (`Audio`, `SoundSim`, `SoundSwap`, `SwapWindow`), 3D (`Viewer3D`, `Lights`, `Smoke`, `Scenery`), repair (`Repair`, `SelfTest`), UI (`Gui`, `MainWindow.xaml`) |
| `installer/` | Inno Setup script and wizard artwork |
| `assets/` | Logo and icon |
| `docs/` | README banner, screenshots and animations |

## 📋 Update log

What changed in every version, with a download link for each one: **[CHANGELOG.md](CHANGELOG.md)**.

## 📜 Licence

Free to use, share and modify. Modified versions must keep the copyright notice, must be renamed (not "Loco Tester"), must not use the logo or the name Piscongentine, must say they are based on Loco Tester, and must not be sold. See [LICENSE.txt](LICENSE.txt).

---

<p align="center">
  Made with ❤️ by <b>Piscongentine</b> · Made in Bharat 🇮🇳<br>
  <sub>Open Rails and MSTS are the property of their respective owners. DB Tracks are by their authors and are not included.</sub>
</p>
