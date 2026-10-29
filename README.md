# Quasar Light

One lightweight, open-source place to see and control everything on your PC that glows.

<p align="center"><img src="docs/app_shot.png" width="760" alt="Quasar Light UI"></p>

<sub>Screenshot of the real app on the developer's PC (GTX 1080 Ti, 2× Kingston KHX3000C15, MSI MPG Z390). Only the graphics card is linked to hardware in this setup, see Status.</sub>

## What works

- Detects **GPU** (name, 64-bit VRAM, driver), **RAM modules** (slot, maker, part, size, speed) and **motherboard** from the registry and SMBIOS — no kernel driver.
- Live animated preview of GPU, RAM and board lighting; 6 effects computed by the C++ engine.
- **Real hardware control through [OpenRGB](https://openrgb.org)** (separate program, talked to over its local SDK server on `127.0.0.1:6742`; no code is linked, so licences stay separate). Quasar Light maps each preview device to the matching OpenRGB device, shows a **LIVE** badge, and sends the chosen effect with *Apply to hardware* (or *Auto-apply*). *Restore* puts the original mode back.
- Effects map to the device's own hardware modes (Static→Direct/Static, Breathing, Spectrum/Color Cycle…). If a device lacks an effect, the primary colour is shown statically and the status bar says so.
- Tray icon, single instance, settings in `%APPDATA%\QuasarLight\settings.json`.
- Bus-safety layer (SPD/PMIC write protection, write grants, audit log) for future native drivers — see [docs/SAFE_SCANNING.md](docs/SAFE_SCANNING.md).

## Status (what is actually verified)

| Item | State |
|---|---|
| Core: detection, effects, guarded bus, OpenRGB protocol parser | 46 automated checks pass |
| OpenRGB client vs. a live OpenRGB 1.1 server | verified: handshake (protocol 3), device + mode parsing, apply, restore |
| Gigabyte GTX 1080 Ti Xtreme (via NVIDIA driver, no extra driver) | commands accepted and acknowledged; **visual change on the card is to be confirmed by the user** |
| Motherboard (MSI Z390) and RAM | **not controllable here**: OpenRGB needs the PawnIO driver for the SMBus/SuperIO; not installed. KHX3000C15 RAM may have no RGB at all |
| Other USB devices (Logitech, JBL, …) | seen by OpenRGB's HID layer but not registered as RGB devices; not shown in the UI yet |

## Build (Windows x64)

Requires MSYS2 mingw64 `g++` and .NET SDK 10.

```powershell
./build-core.ps1                      # builds qlcore.dll, runs core tests
cd ui/QuasarLight.App
dotnet build -c Release -p:Platform=x64
```

To use hardware control: download OpenRGB from its [official releases](https://codeberg.org/OpenRGB/OpenRGB/releases), run Quasar Light and press **Connect OpenRGB** (it starts the server, or asks where `OpenRGB.exe` is once).

## Docs
[Architecture](docs/ARCHITECTURE.md) · [Safe scanning](docs/SAFE_SCANNING.md)

## License
MIT. OpenRGB is GPL-2.0 and is used only as a separate process.
