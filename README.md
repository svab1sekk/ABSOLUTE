# ABSOLUTE

Native Windows 10/11 desktop utility. Self-contained .NET, custom window controls, black and purple.

[Download Windows x64](https://github.com/svab1sekk/ABSOLUTE/releases/download/v0.3.0-preview.1/ABSOLUTE-win-x64.zip) · [Release](https://github.com/svab1sekk/ABSOLUTE/releases/tag/v0.3.0-preview.1)

Extract the whole ZIP and launch `ABSOLUTE.exe`. Keep its folders together. Windows 10 x64 and Windows 11 are supported; ARM64 is cross-built in CI. Releases are unsigned previews.

## Drivers

Drivers shows CPU, GPUs, the motherboard, and identifiable keyboards, mice and connected monitors as component cards. The full PnP inventory is used internally for matching updates. Audio endpoints, CPU-thread records, virtual adapters and generic input-device rows are not listed.

The motherboard groups applicable chipset, PCI storage, Ethernet, Wi-Fi and physical Bluetooth driver offers. Windows Update checks run in the background. **Download** retrieves matching offers into the Windows Update cache; installation remains in Windows Update. Packages with an unaccepted vendor license direct you to Windows Update. Drivers are not automatically installed.

Motherboard packages opens the manufacturer's model page or search, where available. CPU, GPU, monitor and peripheral cards have official manufacturer links. Windows Update is not a universal catalog of the newest OEM-only drivers; “no update offered” does not claim that every vendor release has been checked. Existing Windows Update/WSUS policies are respected.

## PC

- CPU: model, cores and threads. WMI `MaxClockSpeed` is not labelled as the advertised boost clock.
- GPU: model and DXGI dedicated video memory, or shared memory when no dedicated allocation is reported.
- RAM: one combined capacity/type entry, identifiable brand or product family, usable memory and reported configured speed. Module slots and DIMM labels are omitted. Product-family recognition uses the reported part number; it does not invent a kit model.
- Motherboard: model and BIOS version.
- Storage: physical drives, capacity, and SSD/HDD/NVMe classification when Windows exposes it.
- Peripherals: specific product names from raw-input/HID descriptors and bus-reported device names. Firmware that reports only a generic name or receiver cannot establish the exact attached model, so that entry is omitted.
- Monitors: active display instances, EDID product names and manufacturer codes, with current mode when the display interface can be matched, otherwise EDID's preferred mode. Duplicate reports are merged using EDID identity; separate connected displays are retained. TITAN ARMY P25S Plus has an explicit model alias.
- Audio: identifiable physical USB/Bluetooth audio products, once per device container. Generic endpoints, motherboard audio controllers, HDMI/DisplayPort endpoints and monitor/jack entries are excluded.

PC has no serial/revision rows, volume cards, network-adapter cards, driver versions, architecture or color-depth fields. Memory totals stay in the RAM card.

## Temperatures

LibreHardwareMonitorLib **0.9.6** runs inside a separate ABSOLUTE helper. The verified upstream **PawnIO 2.1.0** installer is included in the ZIP and installs automatically if needed. No separate hardware-monitor application or manual download is required. Windows requests administrator permission for the helper after the main window renders. Cancelling that Windows prompt leaves restricted sensors unavailable; it does not prevent using the app.

The pipe accepts readings only from the launched helper and is restricted to the current user, administrators and SYSTEM. The elevated helper validates the parent's executable, session, process start time and pipe-server PID. It receives no execution commands. It exits with the app. No ABSOLUTE background service is installed.

CPU package/die readings are preferred; distance-to-TjMax is excluded. GPU core readings are preferred over hotspot/junction readings. Unsupported hardware and virtual machines may expose no temperature even when the bundled monitor initializes correctly. Existing NVML and monitor WMI providers remain fallbacks. Windows security settings and driver-signature enforcement are kept enabled.

## Home, Presets and Playbooks

Home retains live CPU/GPU/RAM/network readings and adds shortcuts to Windows Game Mode, startup apps, storage cleanup and Windows Update.

Presets uses publisher-supplied Steam library images and official publisher cover images, fetched automatically and cached locally. The selected game has a large faded header. Custom games retain a plain card unless official artwork is supplied. Game profiles detect processes while ABSOLUTE runs, apply an available High performance plan and eligible Above Normal priority, then restore their own changes on game exit, profile switch, or application exit. Protected games can restrict priority changes. Game files are unchanged.

Playbooks contains AtlasOS, ReviOS, AME 11, FSOSX, tiny11builder and MicroWin, with concise descriptions, maker credits and official project links. It has no banners or ABSOLUTE Competitive/Balanced/Focus/Streaming profiles. Settings can restore a legacy profile applied by an older version; that recovery uses its original settings journal and never invokes a full System Restore.

System and Network remain empty. CPU hashing, hardware Direct2D GPU rendering and RAM-copy benchmarks retain measured workload results and cancellation.

## Development

Use the existing checkout and .NET 8 SDK. Linux can compile with Windows targeting and run the shared tests; the WPF app, sensors, HID, EDID and Windows Update require Windows.

```powershell
dotnet restore ABSOLUTE.sln
dotnet build ABSOLUTE.sln -c Release
dotnet test tests/Absolute.Core.Tests -c Release
```

On Windows, prepare the bundled driver before running locally:

```powershell
./scripts/prepare-dependencies.ps1
dotnet run --project src/Absolute.App --runtime win-x64
./scripts/publish.ps1
```

The preparation script verifies the pinned installer's SHA256 and Authenticode signature. Publishing produces `artifacts/ABSOLUTE-win-x64.zip` with its dependencies and third-party notices. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for licenses and source links.

Windows smoke checks verify inventory, combined RAM, the bundled sensor driver and helper initialization, real game-profile rollback, the hardware Direct2D workload, and page rendering at two window sizes. Publisher-image results are recorded separately, including offline or blocked sources. A disposable VM cannot validate physical CPU temperatures, every vendor's HID firmware, or ARM64 runtime behavior. Live Windows Update download/install behavior depends on the target PC's policy and available offers.

The release workflow builds the triggering source commit, requires tests and Windows smoke success, then publishes the ZIP, SHA256 checksum, screenshots and validation report. ReadyToRun and folder distribution avoid single-file extraction; discovery and online work follow the first rendered frame.
