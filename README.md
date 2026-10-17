# ABSOLUTE

Native Windows 10/11 desktop utility. Black and purple, custom window controls, and no bundled browser.

[Download Windows x64](https://github.com/svab1sekk/ABSOLUTE/releases/download/v0.2.0-preview.2/ABSOLUTE-win-x64.zip) · [Release page](https://github.com/svab1sekk/ABSOLUTE/releases/tag/v0.2.0-preview.2)

Extract the entire ZIP and launch `ABSOLUTE.exe`. .NET is included. Windows 10 version 1607+ and Windows 11 are supported; ARM64 builds are available from the Windows workflow. Releases are unsigned previews.

## Pages

| Page | Behavior |
| --- | --- |
| Home | Live CPU, GPU 3D engine, usable RAM, network rates, uptime, and exposed CPU/GPU temperatures. |
| System / Network | Intentionally empty. |
| Presets | Three-column competitive-game gallery, search, and Add game. Automatic detection works while ABSOLUTE is running. |
| Playbooks | Reversible ABSOLUTE settings profiles and a catalog of external Windows projects with official links. |
| Benchmark | Select CPU, GPU 2D, and/or RAM. Results are actual workload throughput rather than an overall score. |
| PC | Separate cards for processors, graphics, memory modules, motherboard, firmware, storage, adapters, peripherals, and available cooling sensors. |
| Drivers | All installed signed-driver records, hardware IDs, Windows Update offers, and manufacturer support links. |
| Settings | Hardware refresh, game detection, logo animation, profile recovery, sensor reconnection, and local result history. |

## Game profiles

The catalog contains 32 games, including Fortnite, CS2, VALORANT, League of Legends, Overwatch 2, Rocket League, Apex Legends, Siege, Dota 2, PUBG, Deadlock, Marvel Rivals, Warzone, THE FINALS, Delta Force, fighting games, and competitive racing/sports games. Add game supports further executables.

Profiles select an existing High performance Windows power plan when available and raise an eligible game process to Above Normal priority. They do not edit game files, affinity masks, security features, or in-game graphics. Games with protected processes may refuse priority changes. No unavailable plan is invented. GPU switching settings are not advertised as live changes because Windows applies those on a subsequent game launch.

The rollback journal is saved before changes. Settings restore on game exit, profile switch, or application exit; an interrupted session is recovered on the next launch. A PID and process start time prevent restoring the priority of an unrelated reused process. External power-plan/priority changes are preserved.

## Settings playbooks

Competitive, Balanced, Focus, and Streaming are **ABSOLUTE settings profiles**, not unofficial copies of third-party OS installations. Activation requests Windows UAC, creates and verifies a System Restore point, and then changes only whitelisted per-user Game Mode, capture, and notification settings. Windows may require a game/session restart for some settings.

Switching first restores the previous profile's exact values using the journal under `HKCU\Software\Absolute\Playbooks`. Values changed outside ABSOLUTE cause a conflict and prevent a restore instead of being overwritten. Windows restore points are emergency backups only: ABSOLUTE never invokes full System Restore or deletes/modifies user folders to switch profiles.

System Protection must be enabled. A failed restore-point operation prevents activation. The 24-hour restore-point frequency value is temporarily changed for the checkpoint and restored in `finally`.

External cards include AtlasOS, ReviOS, FSOSX, AME 11, tiny11builder, MicroWin, WinUtil, Sophia Script, privacy.sexy, Optimizer, WinSlap, Win11Debloat, UnattendedWinstall, and Rectify11. Cards distinguish playbooks, OS image builders, configuration tools, and customization. Full installations can modify Windows components/files and therefore open the official project instead of running under the no-folders rule.

## Sensors, cooling, and drivers

NVIDIA temperature uses the installed NVML driver library when available. LibreHardwareMonitor/OpenHardwareMonitor WMI providers can supply CPU/GPU temperatures, fan speed, flow, or pump sensors. Unsupported or inaccessible sensors show unavailable. No ACPI zone is mislabelled as CPU die temperature, and no temperature is estimated. Use Sensor setup to open the official monitor release and reconnect its running provider. Vendor-specific water-cooler telemetry is available only if exposed by a provider.

Driver discovery uses Windows PnP/signed-driver WMI records. Windows Update is queried online in the background after the first window renders; its existing service/policy settings are respected. An applicable update is matched by hardware ID. No offer from Windows Update does **not** prove that the newest vendor-only release is installed. OEM support links are provided for peripherals and vendor-specific software. Drivers are never automatically installed.

## Benchmarks

- CPU: parallel SHA-256 throughput, all logical processors, about 4 seconds.
- GPU: a real hardware Direct2D scene with 1,600 translucent sprites, immediate presentation, about 4.5 seconds. The result is render-pipeline FPS at the reported resolution, not a pure GPU compute or in-game FPS score. Hardware rendering is required; unsupported sessions report unavailable.
- RAM: single-threaded 32 MB memory-copy throughput, about 4 seconds.

Repeat runs on the same PC and settings for meaningful comparisons. The last 60 component results stay locally at `%LOCALAPPDATA%\Absolute\preferences.json`; old memory-copy history is preserved.

## Develop and test

Use the existing checkout and .NET 8 SDK. Windows is required for the WPF app and Windows services.

```powershell
dotnet restore ABSOLUTE.sln
dotnet build ABSOLUTE.sln --configuration Release
dotnet test tests/Absolute.Core.Tests --configuration Release
dotnet run --project src/Absolute.App
```

Shared tests also run on Linux/macOS. They exercise settings rollback/conflicts, partial failures, persistence, cancellation, and actual CPU/RAM workloads.

```powershell
./scripts/publish.ps1
./scripts/publish.ps1 -Runtime win-arm64
```

Outputs go to `artifacts/`. ReadyToRun and folder distribution avoid single-file extraction. Hardware discovery, sensor connections, and driver checks run after the first window renders. The short logo animation respects Windows reduced-motion settings.

```powershell
$app = Start-Process ./artifacts/ABSOLUTE-win-x64/ABSOLUTE.exe -ArgumentList '--smoke-test', './artifacts/smoke' -PassThru
$app.WaitForExit()
$app.ExitCode
```

Windows smoke checks render all nine pages at two sizes, verify live readings and hardware/driver inventory, exercise and restore an actual game-process/power profile on the disposable runner, and attempt the hardware Direct2D test. Unavailable GPU hardware is reported separately. Online driver searches and real restore-point activation depend on the target PC and are not claimed as verified by CI. Smoke runs do not apply an OS playbook or alter user folders.

The release workflow builds the triggering source commit, checks its version/tag, validates Windows smoke results, and uploads the fresh ZIP, checksum, and page screenshots. ARM64 is cross-built and needs real ARM64 hardware for runtime verification.
