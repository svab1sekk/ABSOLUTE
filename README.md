# ABSOLUTE

Native Windows 10/11 desktop utility. Self-contained .NET, custom window controls, black and purple. The larger title bar has spaced controls and a red close-button hover. Scrollbars are hidden; wheel, touchpad and keyboard scrolling remain available.

[Download Windows x64](https://github.com/svab1sekk/ABSOLUTE/releases/download/v0.5.0-preview.1/ABSOLUTE-win-x64.zip) · [Release](https://github.com/svab1sekk/ABSOLUTE/releases/tag/v0.5.0-preview.1)

Extract the whole ZIP and launch `ABSOLUTE.exe`. Keep its folders together. Windows 10 x64 version 1809 or newer and Windows 11 are supported; ARM64 is cross-built in CI. Releases are unsigned previews.

## Drivers

Connected monitors have their own row; CPU, GPU and motherboard are grouped below. Keyboard, mouse, audio and the full device table are omitted. Installed driver information stays internal except for the matching component's version.

Checks run after startup, on request, and every four hours while ABSOLUTE is open. There is no Windows Update driver search, download or installation. NVIDIA checks use NVIDIA's live product and driver catalogs, with Windows INF versions converted to NVIDIA's public version format. ASUS motherboards use ASUS's model/Windows catalogs for chipset, LAN, wireless, Bluetooth and storage packages. Model-matched AMD/Intel pages and vendor SoftwareApplication metadata can provide versioned downloads when published. Package downloads open the manufacturer's HTTPS link; they are never run automatically.

A chipset bundle version cannot safely be compared to an arbitrary component's INF version. Those packages are labelled for comparison in the vendor installer. The compact cards use a Drivers link and show Download when a package is available. TITAN ARMY monitor links are resolved from the official product catalog/sitemaps and verified against the exact model; search-result pages are never used as their destination. If the model cannot be verified, its link stays unavailable. An unavailable comparison is never labelled current. CPU support is supplied through motherboard chipset packages. BIOS flashing is not performed.

## PC

Displays appear first, then core components, then physical storage. No audio, mouse, keyboard, volume or network cards are shown.

- CPU: model, cores and threads.
- GPU: model and dedicated VRAM, or shared memory if applicable. An embedded PCI lookup helps identify a basic display adapter after a fresh Windows install.
- RAM: one combined capacity/type card, identifiable brand/family, usable memory and configured speed.
- Motherboard: model and BIOS version.
- Storage: model, capacity and SSD/HDD/NVMe classification where exposed.
- Monitors: active EDID identities, current display mode where matched, otherwise preferred mode. Duplicate reports of one physical display are merged; separate connected displays are retained. TITAN ARMY P25S Plus has an explicit model alias.

No serials, revisions, driver versions, architecture or color-depth fields appear on PC. Memory totals stay inside RAM. Hardware cards have bounded widths, nearby label/value pairs and centered incomplete rows, including an odd last storage card.

## Temperatures

LibreHardwareMonitorLib **0.9.6** runs inside a separate ABSOLUTE helper. The verified upstream **PawnIO 2.1.0** installer is included in the ZIP and installs automatically if needed. No separate hardware-monitor application or manual download is required. Windows requests administrator permission for the helper after the main window renders. Cancelling that Windows prompt leaves restricted sensors unavailable; it does not prevent using the app.

The pipe accepts readings only from the launched helper and is restricted to the current user, administrators and SYSTEM. The elevated helper validates the parent's executable, session, process start time and pipe-server PID. It receives no execution commands. It exits with the app. No ABSOLUTE background service is installed.

CPU package/die readings are preferred; distance-to-TjMax is excluded. GPU core readings are preferred over hotspot/junction readings. Unsupported hardware and virtual machines may expose no temperature even when the bundled monitor initializes correctly. Existing NVML and monitor WMI providers remain fallbacks. Windows security settings and driver-signature enforcement are kept enabled.

## Home, Presets and Playbooks

Home has CPU, GPU and RAM graphs with utilization and available temperatures. Network/IP and machine/uptime panels are removed. Quick access and the existing Windows tools remain.

The catalog includes 158 competitive, multiplayer, sports, racing and practice titles. Steam library manifests supplement process detection so games sharing an engine executable can be distinguished by their actual installation. Manifests and game folders are only read.

Click a compact game tile to configure its power plan and process priority. Detection is always automatic while ABSOLUTE runs. Settings apply to the detected process and restore on game exit, switching game or closing ABSOLUTE. Protected games may restrict priority changes. Game files are unchanged. Official artwork refreshes after 24 hours; Epic's live season background/news and Riot's current game-update articles replace stale static covers. Cached images remain usable offline. Card artwork is decoded at a smaller resolution than the large selected-game banner to bound memory use across the expanded catalog.

Playbooks lists 22 verified Windows projects: AtlasOS, ReviOS, AME 11, FSOSX, AtmosphereOS, RapidOS, AtomOS, ArkanoidOS Lite, UltraOS, tiny11builder, MicroWin, SynergyOS, NOVA, AkatiOS, ShimmerOS, ZLAG OS, KiyomizuOS, ThinNekoOS, ReimaginedOS, RivalsOS, Nexulation and KutayOS. Each card has the project's official profile image, maker and three short facts, linking to its project. Image builders are separate project types, not presented as runnable AME playbooks. No third-party OS project is executed inside ABSOLUTE. Legacy ABSOLUTE settings journals can still be restored in Settings.

System and Network remain empty. Startup shows a short centered logo rotation followed by movement into the sidebar; hardware discovery runs after the first frame.

## Benchmark

A run combines CPU SHA-256 throughput, a hardware Direct3D 11 scene, and a 256 MB RAM-copy workload (512 MB total working set). Hardware Direct2D is optional. The 3D scene renders 1,024 animated lit cubes with depth testing and procedural pixel shading at a fixed 1280 × 720, after a one-second warm-up. Software rendering is not scored. Progress identifies the active component and elapsed measurement time. Cancellation saves a visibly partial run.

Each run appears as one expandable result with component measurements, interpretation and an overall score. Suite 1 uses explicit workload targets: 100 points equals 1,024 MB/s CPU hashing, 8,192 MB/s memory copy, or 60 FPS in the fixed 3D scene. The combined score is the geometric mean across measured CPU/GPU/RAM categories; optional 2D does not double-weight the GPU. Ratings are Low, Below target, Normal, Good and Excellent. They are workload grades, not population percentiles, hardware health diagnoses or predicted game FPS. Partial runs are labelled and should not be compared with full runs.

## Development

Use the existing checkout and .NET 8 SDK. Linux can compile with Windows targeting and run the shared tests; the WPF app, sensors, EDID and Direct3D require Windows.

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

Windows smoke checks verify inventory exclusions, combined RAM, the sensor helper, game-profile rollback, actual CPU/RAM workloads, 2D/3D graphics where the runner exposes hardware, shader compilation, startup rendering and pages/editors/results at two window sizes. Artwork statuses and unsupported GPU rendering are reported explicitly. A VM cannot validate physical CPU temperatures, every manufacturer's live catalog, or ARM64 runtime behavior.

The release workflow builds the triggering source commit, requires tests and Windows smoke success, then publishes the ZIP, SHA256 checksum, screenshots and validation report. ReadyToRun and folder distribution avoid single-file extraction; discovery and online work follow the first rendered frame.
