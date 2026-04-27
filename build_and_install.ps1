<#
.SYNOPSIS
    Build and install the GameMover plugin for Playnite.

.DESCRIPTION
    1. Locates Playnite.SDK.dll from your Playnite installation.
    2. Builds the project with MSBuild / dotnet CLI.
    3. Copies the output into your Playnite Extensions folder.

.EXAMPLE
    .\build_and_install.ps1
    .\build_and_install.ps1 -PlaynitePath "D:\Apps\Playnite"
#>

param(
    # Path to your Playnite installation folder (where Playnite.exe lives).
    # Leave blank to auto-detect from the default AppData location.
    [string]$PlaynitePath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── 1. Locate Playnite ──────────────────────────────────────────────────────
if (-not $PlaynitePath)
{
    # Try common locations
    $candidates = @(
        "$env:LOCALAPPDATA\Playnite",
        "$env:APPDATA\Playnite",
        "C:\Program Files\Playnite",
        "C:\Program Files (x86)\Playnite"
    )
    foreach ($c in $candidates)
    {
        if (Test-Path (Join-Path $c "Playnite.exe"))
        {
            $PlaynitePath = $c
            break
        }
    }
}

if (-not $PlaynitePath -or -not (Test-Path (Join-Path $PlaynitePath "Playnite.exe")))
{
    Write-Error @"
Could not find Playnite.exe.
Please pass -PlaynitePath pointing to the folder that contains Playnite.exe.
Example:
    .\build_and_install.ps1 -PlaynitePath "C:\Games\Playnite"
"@
    exit 1
}

Write-Host "✅ Found Playnite at: $PlaynitePath" -ForegroundColor Green

# ── 2. Copy Playnite.SDK.dll into lib\ ─────────────────────────────────────
$sdkSource = Join-Path $PlaynitePath "Playnite.SDK.dll"
if (-not (Test-Path $sdkSource))
{
    Write-Error "Playnite.SDK.dll not found inside '$PlaynitePath'. Make sure the path is correct."
    exit 1
}

$libDir = Join-Path $PSScriptRoot "lib"
New-Item -ItemType Directory -Force -Path $libDir | Out-Null
Copy-Item $sdkSource $libDir -Force
Write-Host "✅ Copied Playnite.SDK.dll -> lib\" -ForegroundColor Green

# ── 3. Build ────────────────────────────────────────────────────────────────
Write-Host "`n🔨 Building plugin..." -ForegroundColor Cyan

# Prefer dotnet CLI, fall back to msbuild
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source
$msbuild = (Get-Command msbuild -ErrorAction SilentlyContinue)?.Source

if ($dotnet)
{
    & $dotnet build "$PSScriptRoot\GameMover.csproj" -c Release
}
elseif ($msbuild)
{
    & $msbuild "$PSScriptRoot\GameMover.csproj" /p:Configuration=Release
}
else
{
    Write-Error "Neither 'dotnet' nor 'msbuild' found. Please install the .NET SDK (https://dotnet.microsoft.com/download)."
    exit 1
}

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# ── 4. Locate output DLL ─────────────────────────────────────────────────────
$dll = Join-Path $PSScriptRoot "bin\Release\GameMover.dll"
if (-not (Test-Path $dll))
{
    Write-Error "Build succeeded but GameMover.dll was not found at: $dll"
    exit 1
}

# ── 5. Install into Playnite Extensions ──────────────────────────────────────
# Playnite looks for extensions in:  %APPDATA%\Playnite\Extensions\<ExtensionFolder>\
$extensionsRoot = Join-Path $env:APPDATA "Playnite\Extensions"
$destFolder     = Join-Path $extensionsRoot "GameMover"

New-Item -ItemType Directory -Force -Path $destFolder | Out-Null

Copy-Item $dll                                             (Join-Path $destFolder "GameMover.dll")         -Force
Copy-Item (Join-Path $PSScriptRoot "src\extension.yaml")  (Join-Path $destFolder "extension.yaml")        -Force

Write-Host "`n✅ Plugin installed to:" -ForegroundColor Green
Write-Host "   $destFolder" -ForegroundColor Yellow
Write-Host "`n👉 Please RESTART Playnite to load the plugin." -ForegroundColor Cyan
Write-Host "   Then right-click any game → you should see '改变存储位置'." -ForegroundColor Cyan
