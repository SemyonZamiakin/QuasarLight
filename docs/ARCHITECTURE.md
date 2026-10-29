# Architecture

```
┌────────────────────────────── UI (WPF, .NET) ──────────────────────────────┐
│ tray · device list · live preview · effect/colour controls · settings.json  │
└───────────────▲──────────────────────────────────────────────▲──────────────┘
                │ C ABI (qlcore.h, P/Invoke, versioned)         │
┌───────────────┴────────────────────── core (C++17) ───────────┴──────────────┐
│ sysinfo     GPU (registry) + RAM/board (SMBIOS)        — no driver needed     │
│ effects     pure function  f(params, t, n) -> n×RGB     — deterministic       │
│ GuardedBus  the ONLY path to hardware: policy + audit + rate limit            │
│ scanBus     read-only enumeration through GuardedBus                          │
├───────────────────────────────────────────────────────────────────────────────┤
│ IBus  (transport)        ◄── implemented OUTSIDE the core (driver packages)   │
│ IDriver plugins (planned) detect(GuardedBus&) -> IDevice, setColors(...)      │
└───────────────────────────────────────────────────────────────────────────────┘
```

## Layers and why

| Layer | Responsibility | Why separate |
|---|---|---|
| UI | Presentation only. Never touches a bus. | Replaceable (Qt/CLI) without touching hardware code. |
| C ABI | `ql_system_json`, `ql_render`, … | Stable boundary; UI crash can't corrupt core state; UI in any language. |
| Effects | Pure maths, no I/O. | Unit-testable, identical output for preview and real hardware. |
| GuardedBus | Safety policy, see [SAFE_SCANNING.md](SAFE_SCANNING.md). | One chokepoint to audit instead of N plugins. |
| IBus | Raw transport (SMBus/I²C, USB HID). | Kernel-driver specifics isolated; mockable (`SimBus` in tests). |
| Driver plugins | Identify one vendor's device, translate colours to its protocol. | New hardware = new plugin, no core change. |

## Preview vs. hardware

The preview/effect pipeline is complete and tested. Hardware output goes through the **OpenRGB backend**
(`core/src/openrgb.cpp`): a TCP client for the OpenRGB SDK server on loopback (protocol 3). OpenRGB owns the
drivers and per-vendor protocols; Quasar Light owns the UI, effects and settings. Device matching is by OpenRGB
device type (GPU / DRAM / motherboard). Without a running server the UI stays in preview mode.

Native drivers (own SMBus transport + plugins behind `GuardedBus`) remain an option for devices OpenRGB cannot reach.

## Repository layout

```
core/include/ql/   public headers (bus, guard, effects, sysinfo, qlcore C ABI)
core/src/          implementation
core/tests/        host tests incl. simulated SMBus
ui/QuasarLight.App WPF app (net10.0-windows, x64)
build-core.ps1     builds qlcore.dll + runs core tests
docs/              architecture, safety, screenshot
```

## Footprint (measured on the dev machine, Release)

~130 MB working set and ~25-30 % of one core while the window is open and animating (blur-heavy scene).
That misses the "minimal RAM" goal; the planned fix is a tray-only service process (no WPF, <10 MB) with the
UI started on demand. Hidden-in-tray rendering is skipped, but idle tray CPU was **not measured**.
