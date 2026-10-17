# Third-party components

ABSOLUTE includes unmodified LibreHardwareMonitorLib 0.9.6 (MPL-2.0), HidSharp 2.6.4 (Apache-2.0), and their NuGet dependencies. LibreHardwareMonitor's license and notices are in `ThirdParty/`. Its corresponding source is available at:

https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701

The bundled, unmodified PawnIO 2.1.0 installer is supplied by that upstream release. Copyright namazso. PawnIO is GPL-2.0-or-later with its documented exception; see `ThirdParty/PawnIO-COPYING.txt`. Driver source, build instructions, and source archives:

https://github.com/namazso/PawnIO/tree/e1043d7750f501b6b0d3ac0a71bd5b9406cd17b6

https://github.com/namazso/PawnIO.Modules/releases/tag/0.1.6

The installer is pinned by SHA256 and its Authenticode signature is verified during packaging. ABSOLUTE never disables Windows driver-signature enforcement or the vulnerable-driver blocklist.

Game artwork is retrieved from publishers' official game pages and Steam's publisher-supplied library images, and cached locally. The images and game names belong to their respective publishers. Source mappings are in `src/Absolute.Core/GameArtwork.cs`; ABSOLUTE does not claim ownership or affiliation.

Vortice.Windows and SharpGen.Runtime use the MIT license. .NET and Microsoft runtime components retain their upstream notices included with the self-contained distribution. Other NuGet dependency licenses are available through each package's metadata.
