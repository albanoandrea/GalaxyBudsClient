<#
.SYNOPSIS
    Installs everything needed to build GalaxyBudsClient on a fresh Windows machine.

.DESCRIPTION
    - Installs the .NET SDK feature band pinned in global.json (per-user, no admin required)
      into %LOCALAPPDATA%\Microsoft\dotnet and adds it to the user PATH.
    - Optionally installs NSIS (needed only to build the setup installer from install.nsi).
    - Restores NuGet packages for the solution.

.PARAMETER WithInstaller
    Also install NSIS via winget, to build the .exe installer.

.PARAMETER SkipRestore
    Do not run 'dotnet restore' at the end.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File setup\setup-windows.ps1
    powershell -ExecutionPolicy Bypass -File setup\setup-windows.ps1 -WithInstaller
#>
[CmdletBinding()]
param(
    [switch]$WithInstaller,
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$DotnetDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

# --- Required SDK from global.json -------------------------------------------
$globalJson = Get-Content (Join-Path $RepoRoot 'global.json') -Raw | ConvertFrom-Json
$sdkVersion = [version]$globalJson.sdk.version
# rollForward=latestPatch → any SDK in the same feature band (e.g. 10.0.3xx) is accepted
$featureBand = '{0}.{1}.{2}xx' -f $sdkVersion.Major, $sdkVersion.Minor, [math]::Floor($sdkVersion.Build / 100)
Write-Step "Required .NET SDK: $($globalJson.sdk.version) (feature band $featureBand)"

function Test-SdkInstalled {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) { return $false }
    $prefix = $featureBand.Replace('xx', '')
    $sdks = & $dotnet.Source --list-sdks 2>$null
    foreach ($line in $sdks) {
        $v = ($line -split ' ')[0]
        if ($v.StartsWith($prefix) -and [version]$v -ge $sdkVersion) { return $true }
    }
    return $false
}

function Add-ToUserPath($dir) {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($null -eq $userPath) { $userPath = '' }
    if (($userPath -split ';') -notcontains $dir) {
        [Environment]::SetEnvironmentVariable('Path', ($dir + ';' + $userPath).TrimEnd(';'), 'User')
        Write-Host "    Added $dir to user PATH"
    }
    if (($env:Path -split ';') -notcontains $dir) { $env:Path = "$dir;$env:Path" }
}

# --- .NET SDK ----------------------------------------------------------------
if (Test-SdkInstalled) {
    Write-Step ".NET SDK $featureBand already installed, skipping"
} else {
    Write-Step "Installing .NET SDK $featureBand into $DotnetDir"
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Channel $featureBand -InstallDir $DotnetDir -NoPath
    Remove-Item $installer -Force

    Add-ToUserPath $DotnetDir
    [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $DotnetDir, 'User')
    $env:DOTNET_ROOT = $DotnetDir

    if (-not (Test-SdkInstalled)) { throw ".NET SDK installation failed" }
}
& dotnet --version

# --- NSIS (optional, installer only) -----------------------------------------
if ($WithInstaller) {
    if (Get-Command makensis -ErrorAction SilentlyContinue) {
        Write-Step "NSIS already installed, skipping"
    } else {
        Write-Step "Installing NSIS via winget"
        if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { throw "winget not found: install NSIS manually from https://nsis.sourceforge.io" }
        winget install --id NSIS.NSIS -e --accept-source-agreements --accept-package-agreements
        $nsisDir = Join-Path ${env:ProgramFiles(x86)} 'NSIS'
        if (-not (Test-Path (Join-Path $nsisDir 'makensis.exe'))) {
            throw "NSIS installation failed (winget exit code $LASTEXITCODE). Retry, or install it manually from https://nsis.sourceforge.io"
        }
        if (Test-Path $nsisDir) { Add-ToUserPath $nsisDir }
    }
}

# --- Restore -----------------------------------------------------------------
if (-not $SkipRestore) {
    Write-Step "Restoring NuGet packages"
    $nugetConfig = Join-Path $RepoRoot 'GalaxyBudsClient\nuget.config'
    & dotnet restore --configfile $nugetConfig (Join-Path $RepoRoot 'GalaxyBudsClient\GalaxyBudsClient.csproj')
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
    & dotnet restore --configfile $nugetConfig (Join-Path $RepoRoot 'GalaxyBudsClient.Tests\GalaxyBudsClient.Tests.csproj')
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore (tests) failed" }
}

Write-Step "Done. Open a new terminal so PATH changes take effect, then run setup\build-windows.ps1"
