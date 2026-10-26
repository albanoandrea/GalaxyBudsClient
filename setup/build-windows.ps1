<#
.SYNOPSIS
    Builds, tests or publishes GalaxyBudsClient on Windows (mirrors .github/workflows/compile-windows.yml).

.PARAMETER Mode
    build   - Debug/Release build of the app (default)
    test    - run the unit tests
    publish - self-contained single-file exe in bin\<runtime>
    installer - publish + NSIS setup .exe (requires NSIS, see setup-windows.ps1 -WithInstaller)
    android - signed APK in bin\android (requires setup-windows.ps1 -WithAndroid)
    android-install - android + install the APK on the phone connected via adb

.PARAMETER AndroidRuntime
    Restrict the APK to one ABI (e.g. android-arm64) for a much smaller file. Default: all ABIs.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1
    powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode publish -Runtime win-x64
    powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode installer
    powershell -ExecutionPolicy Bypass -File setup\build-windows.ps1 -Mode android-install -AndroidRuntime android-arm64
#>
[CmdletBinding()]
param(
    [ValidateSet('build', 'test', 'publish', 'installer', 'android', 'android-install')]
    [string]$Mode = 'build',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-x86')]
    [string]$Runtime = 'win-x64',
    [ValidateSet('', 'android-arm', 'android-arm64', 'android-x86', 'android-x64')]
    [string]$AndroidRuntime = '',
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

function Publish-Android {
    # Same flags as upstream GalaxyBudsClient.Android/build.sh: a Debug build with embedded assemblies
    # (avoids Release trimming/AOT issues), not debuggable. Fork overrides (package id, full version,
    # no INTERNET) come from GalaxyBudsClient.Android/Directory.Build.{props,targets}.
    # Signed with the local .NET debug keystore.
    $androidProject = Join-Path $RepoRoot 'GalaxyBudsClient.Android\GalaxyBudsClient.Android.csproj'
    $androidSdk = $(if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' })
    $javaSdk = Join-Path $env:LOCALAPPDATA 'Android\jdk'
    if (-not (Test-Path (Join-Path $javaSdk 'bin\java.exe'))) {
        throw "JDK not found in $javaSdk. Run setup\setup-windows.ps1 -WithAndroid first."
    }
    $outDir = Join-Path $RepoRoot 'bin\android'
    $ridArgs = @()
    # The csproj lists all ABIs in RuntimeIdentifiers (-r is ignored); see GalaxyBudsClient.Android/Directory.Build.targets
    if ($AndroidRuntime) { $ridArgs = @("-p:ForkAndroidRuntime=$AndroidRuntime") }
    # IsAndroid must be global: it doesn't propagate to referenced projects otherwise
    Invoke-Checked dotnet publish $androidProject -f net10.0-android36.1 -c Debug -o $outDir @ridArgs `
        -p:IsAndroid=true -p:AndroidPackageFormats=apk -p:EmbedAssembliesIntoApk=true `
        -p:NotDebuggable=true -p:DebugType=PdbOnly `
        "-p:AndroidSdkDirectory=$androidSdk" "-p:JavaSdkDirectory=$javaSdk"
    return (Get-ChildItem $outDir -Filter '*-Signed.apk' | Select-Object -First 1).FullName
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
    'android' {
        $apk = Publish-Android
        Write-Host "APK created: $apk" -ForegroundColor Green
    }
    'android-install' {
        $adb = $(if ($env:ANDROID_HOME) { Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe' } else { 'adb' })
        $apk = Publish-Android
        Invoke-Checked $adb install -r $apk
        Write-Host "Installed $apk" -ForegroundColor Green
    }
}
