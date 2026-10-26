<#
.SYNOPSIS
    Builds, tests or publishes GalaxyBudsClient on Windows (mirrors .github/workflows/compile-windows.yml).

.PARAMETER Mode
    build   - Debug/Release build of the app (default)
    test    - run the unit tests
    publish - self-contained single-file exe in bin\<runtime>
    installer - publish + NSIS setup .exe (requires NSIS, see setup-windows.ps1 -WithInstaller)

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1
    powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode publish -Runtime win-x64
    powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode installer
#>
[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'publish', 'installer')]
    [string]$Mode = 'build',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-x86')]
    [string]$Runtime = 'win-x64',
    [string]$Version = '0.0.0-local'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot 'GalaxyBudsClient\GalaxyBudsClient.csproj'
$NugetConfig = Join-Path $RepoRoot 'GalaxyBudsClient\nuget.config'
$Tfm = 'net10.0-windows10.0.19041'

function Invoke-Checked {
    # Out-Host keeps command output out of function return values
    & $args[0] $args[1..($args.Length - 1)] | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $($args -join ' ')" }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet not found. Run setup\setup-windows.ps1 first (and open a new terminal)."
}

function Publish($rid) {
    # bin/ is already git-ignored
    $outDir = Join-Path $RepoRoot "bin\$rid"
    Invoke-Checked dotnet restore -r $rid --configfile $NugetConfig $Project
    Invoke-Checked dotnet publish -f $Tfm -r $rid -o $outDir -c $Configuration `
        -p:PublishSingleFile=true --self-contained true --no-restore $Project
    return $outDir
}

switch ($Mode) {
    'build' {
        Invoke-Checked dotnet build -c $Configuration $Project
    }
    'test' {
        Invoke-Checked dotnet test (Join-Path $RepoRoot 'GalaxyBudsClient.Tests\GalaxyBudsClient.Tests.csproj')
    }
    'publish' {
        $out = Publish $Runtime
        Write-Host "Published to $out" -ForegroundColor Green
    }
    'installer' {
        if (-not (Get-Command makensis -ErrorAction SilentlyContinue)) {
            throw "makensis not found. Run setup\setup-windows.ps1 -WithInstaller first."
        }
        $out = Publish $Runtime
        $arch = $(if ($Runtime -eq 'win-x86') { '32bit' } else { '64bit' })
        # install.nsi expects "Galaxy Buds Client.exe" and *.pdb in BASE_DIR
        $stage = Join-Path $RepoRoot 'bin\nsis-stage'
        Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force $stage | Out-Null
        Copy-Item (Join-Path $out 'GalaxyBudsClient.exe') (Join-Path $stage 'Galaxy Buds Client.exe')
        Copy-Item (Join-Path $out '*.pdb') $stage
        try {
            Push-Location $RepoRoot
            Invoke-Checked makensis "-DBASE_DIR=$stage" "-DPRODUCT_VERSION=$Version" "-DPRODUCT_ARCH=$arch" install.nsi
        } finally {
            Pop-Location
            Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host "Installer created in $RepoRoot" -ForegroundColor Green
    }
}
