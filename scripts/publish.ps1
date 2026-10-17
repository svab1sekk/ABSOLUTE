param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    & (Join-Path $PSScriptRoot 'prepare-dependencies.ps1')
    dotnet test tests/Absolute.Core.Tests/Absolute.Core.Tests.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $output = Join-Path $repo "artifacts/ABSOLUTE-$Runtime"
    dotnet publish src/Absolute.App/Absolute.App.csproj --configuration Release --runtime $Runtime --self-contained true -p:PublishReadyToRun=true -p:PublishSingleFile=false --output $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item (Join-Path $repo 'README.md') $output
    Copy-Item (Join-Path $repo 'THIRD_PARTY_NOTICES.md') $output
    Copy-Item (Join-Path $repo 'ThirdParty') $output -Recurse -Force
    Compress-Archive -Path "$output/*" -DestinationPath "$output.zip" -Force
    Write-Host "Built $output.zip"
} finally { Pop-Location }
