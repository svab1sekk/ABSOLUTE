$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $repo 'src/Absolute.App/Dependencies'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$installer = Join-Path $directory 'PawnIO_setup.exe'
$expected = 'a3a46226c5e2824f4cdd42be0eecbabfc672c86f7889710f5ab1e6ad385b47a0'
if (-not (Test-Path $installer) -or (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/3d331e3370efb858411f19511373eff65a218701/LibreHardwareMonitor/Resources/PawnIO_setup.exe' -OutFile $installer
}
if ((Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'The bundled sensor installer failed SHA256 verification.' }
$signature = Get-AuthenticodeSignature $installer
if ($signature.Status -ne 'Valid') { throw "The sensor installer signature is not trusted: $($signature.Status)." }
Write-Host 'Bundled PawnIO 2.1.0 verified: SHA256 and Authenticode.'
