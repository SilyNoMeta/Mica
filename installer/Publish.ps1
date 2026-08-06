<#
.SYNOPSIS
    Publishes Mica into a single payload folder for the installer.

.DESCRIPTION
    The builder and the host must end up side by side: Mica resolves the host
    executable next to itself, and writes each app's launcher into that same folder.

    Published .NET self-contained, so the machine needs no .NET runtime, but with a
    framework-dependent Windows App SDK on purpose — a self-contained SDK layout
    omits Microsoft.WindowsAppRuntime.Insights.Resource.dll, without which
    AppNotificationManager cannot register and web notifications stop working. The
    installer chains the Windows App Runtime instead.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File installer\Publish.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',

    # Stamped into the assemblies. Mica compares this against the latest release tag
    # to decide whether an update exists, so a payload built with the wrong version
    # would either never update or offer an update it already is.
    [string]$Version,

    [string]$Output
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) {
    $Output = Join-Path $root "dist\payload-$Architecture"
}

if (Test-Path $Output) {
    Remove-Item -Recurse -Force -LiteralPath $Output
}
New-Item -ItemType Directory -Force -Path $Output | Out-Null

$platform = if ($Architecture -eq 'arm64') { 'ARM64' } else { 'x64' }

# The builder is published last so that, on any file both projects carry, the copy
# that ships is the one built against the same shared library the builder expects.
foreach ($project in @('Mica.Host', 'Mica.Builder')) {
    Write-Host "Publishing $project ($Architecture)..."

    $versionArgs = if ($Version) { @("-p:Version=$Version") } else { @() }

    dotnet publish (Join-Path $root "$project\$project.csproj") `
        --configuration $Configuration `
        --runtime "win-$Architecture" `
        --self-contained true `
        -p:Platform=$platform `
        -p:DebugType=none `
        @versionArgs `
        --output $Output `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        throw "Publishing $project failed with exit code $LASTEXITCODE."
    }
}

# dotnet publish copies the referenced library's resource index but not the
# application's own, for unpackaged WinUI. Without <executable>.pri the app dies
# inside Microsoft.UI.Xaml before any of its code runs — and Mica needs the host's
# copy anyway, to hand one to every per-app launcher it writes.
foreach ($project in @('Mica.Host', 'Mica.Builder')) {
    $index = Get-ChildItem -LiteralPath (Join-Path $root "$project\bin") -Filter "$project.pri" -Recurse |
        Where-Object { $_.FullName -like "*\$Configuration\*" -and $_.FullName -like "*win-$Architecture*" } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if (-not $index) {
        throw "Cannot find $project.pri in the $Configuration build output."
    }

    Copy-Item -LiteralPath $index.FullName -Destination (Join-Path $Output "$project.pri") -Force
    Write-Host "Added $project.pri"
}

Get-ChildItem -LiteralPath $Output -Filter '*.pdb' -Recurse | Remove-Item -Force

$size = [math]::Round((Get-ChildItem -LiteralPath $Output -Recurse -File |
        Measure-Object -Property Length -Sum).Sum / 1MB, 1)
$count = (Get-ChildItem -LiteralPath $Output -Recurse -File).Count

foreach ($required in @('Mica.Builder.exe', 'Mica.Builder.pri', 'Mica.Host.exe', 'Mica.Host.pri')) {
    if (-not (Test-Path (Join-Path $Output $required))) {
        throw "The payload is missing $required."
    }
}

Write-Host ""
Write-Host "Payload: $Output"
Write-Host "         $count files, $size MB"
