# ABSOLUTE

A native Windows desktop utility with a black-to-purple interface, custom window controls, and local system tools.

## Run on Windows

[Download ABSOLUTE for Windows x64](https://github.com/svab1sekk/ABSOLUTE/releases/download/v0.1.0-preview.1/ABSOLUTE-win-x64.zip) · [Release page](https://github.com/svab1sekk/ABSOLUTE/releases/tag/v0.1.0-preview.1)

Extract the **entire** ZIP and run `ABSOLUTE.exe`. The self-contained build includes .NET; no separate runtime installation is needed. This is an early preview. Additional builds are available from the repository's **Windows** workflow after it passes.

- Windows 10 version 1607 or later, or Windows 11. Use a supported Windows edition and current updates.
- `win-x64` for Intel/AMD 64-bit PCs; `win-arm64` for Windows on ARM.
- No automatic elevation, background service, startup entry, or external account.
- Builds are unsigned. Windows may display a SmartScreen warning until a release is signed with a trusted certificate.

## Features

| Page | Behavior |
| --- | --- |
| System | Live CPU and memory readings, current power plan, uptime, and Windows settings shortcuts. |
| Network | Active adapter, local IP, configured DNS, live transfer rates, and DNS cache flush. |
| Presets | Applies existing Balanced, Performance, or Power saver plans after confirmation. Retains the previous plan for restoration. Unsupported plans are disabled. |
| Playbooks | Gaming, everyday, and maintenance shortcuts to the relevant Windows settings. These are manual workflows. |
| Benchmark | Cancellable four-second, single-threaded memory copy measurement; retains the latest twenty results locally and displays the latest five. |
| PC | CPU, graphics adapter, logical processors, RAM, Windows build, and system-drive capacity. |
| Settings | Pause monitoring, restore the previous plan, or clear benchmark history. |

The app does not silently edit the registry, remove files, disable security features, change DNS servers, or claim FPS improvements. Windows can deny power-plan or DNS operations under device policy; the app displays the returned error. Modern Standby devices commonly expose only Balanced.

## Develop

Install the .NET 8 SDK, then use the existing checkout:

```powershell
dotnet restore ABSOLUTE.sln
dotnet run --project src/Absolute.App
```

Windows is required to run the WPF interface and the Windows integration checks. Shared logic tests also run on Linux and macOS:

```powershell
dotnet build ABSOLUTE.sln --configuration Release
dotnet test tests/Absolute.Core.Tests --configuration Release
```

## Package

```powershell
./scripts/publish.ps1
./scripts/publish.ps1 -Runtime win-arm64
```

Outputs go to `artifacts/`. Publish uses ReadyToRun and a folder distribution, avoiding single-file extraction on launch. The first window renders before hardware discovery and power-plan queries run. Actual cold-start time depends on the PC and must be measured on Windows.

## Windows smoke check

```powershell
Start-Process ./artifacts/ABSOLUTE-win-x64/ABSOLUTE.exe -ArgumentList '--smoke-test', './artifacts/smoke' -Wait
```

The smoke check renders every page at desktop and compact window sizes, writes screenshots and a JSON report, and checks live system readings and power-plan discovery without applying changes. It exits with a nonzero status on failure. The Windows workflow runs it for x64 builds; ARM64 is cross-published and requires a real ARM64 machine for runtime verification.

## Structure

- `src/Absolute.App`: WPF shell, reusable controls, and Windows API/process integration.
- `src/Absolute.Core`: power-plan parsing, local preferences, and memory benchmark.
- `tests/Absolute.Core.Tests`: parsing, persistence, cancellation, and real benchmark tests.

Settings and benchmark history live at `%LOCALAPPDATA%\Absolute\preferences.json`. No telemetry or network service is used. The app launches Windows Settings for manual maintenance; the DNS tool only clears the local resolver cache.

The supplied logo has a darker derivative in `Assets/logo.png` and a multi-resolution Windows icon. UI glyphs are resolution-independent vectors; WPF uses per-monitor DPI scaling and the Windows Segoe font family.
