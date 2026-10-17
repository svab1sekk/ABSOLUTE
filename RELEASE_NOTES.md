# ABSOLUTE 0.2.0 Preview

- New Home dashboard with live CPU, GPU, RAM, network, and exposed temperature sensors.
- System and Network are intentionally empty for the next round of changes.
- 32 competitive-game cards with custom banners, search, custom-game support, and automatic Windows profiles while ABSOLUTE is running.
- Game profiles apply an available performance power plan and Above Normal process priority when Windows permits; original settings are restored afterwards.
- 18 settings profiles and Windows projects, including AtlasOS, ReviOS, FSOSX, AME, tiny11, MicroWin, WinUtil, Sophia Script, and privacy.sexy.
- ABSOLUTE settings playbooks require a Windows restore point and use a settings journal to switch or restore only their own changes. They never invoke a full System Restore or modify game/user folders.
- Full external OS projects open their official sites. Installing them would conflict with the settings-only, no-folders rule.
- Selectable CPU hashing, hardware Direct2D GPU rendering, and RAM-copy benchmarks with real measured results and cancellation.
- Individual hardware cards, usable RAM, storage, network adapters, peripherals, and exposed cooling telemetry. No processor trademark noise or Windows build number.
- Installed-driver inventory, background Windows Update searches, and manufacturer support links. No automatic driver installation or unsupported “everything is current” claims.
- Short centered logo rotation and movement into the sidebar; the logo's crossbar is much darker.

Extract the entire ZIP and run **ABSOLUTE.exe**. All required .NET libraries are included. CPU/cooling telemetry needs a compatible sensor provider; NVIDIA GPU temperature can use the installed driver. Missing sensors remain unavailable. Windows Update may not offer vendor-only drivers. System Protection must be enabled before activating a settings playbook.
