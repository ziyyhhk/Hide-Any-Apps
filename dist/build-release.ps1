# Hide Any Apps - release build
# Run from anywhere: powershell -ExecutionPolicy Bypass -File dist\build-release.ps1
#
# Outputs:
#   dist\staging\manager\...          self-contained manager
#   dist\staging\service\...          compiled AHK service
#   dist\HideAnyApps-Setup.exe        per-user installer (unsigned)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$staging  = Join-Path $repoRoot 'dist\staging'

function Find-Tool([string] $local, [string] $fallback) {
    if (Test-Path $local) { return (Resolve-Path $local).Path }
    $cmd = Get-Command $fallback -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$dotnet  = Find-Tool (Join-Path $repoRoot '.tools\dotnet\dotnet.exe') 'dotnet.exe'
$ahk2Exe = Find-Tool (Join-Path $repoRoot '.tools\ahk2exe\Ahk2Exe.exe') 'Ahk2Exe.exe'
$ahkBase = Find-Tool (Join-Path $repoRoot '.tools\ahk\AutoHotkey64.exe') 'AutoHotkey64.exe'
$iscc    = Find-Tool (Join-Path $repoRoot '.tools\innosetup\ISCC.exe') 'ISCC.exe'
if (-not $ahk2Exe -and (Test-Path 'C:\Program Files\AutoHotkey\Compiler\Ahk2Exe.exe')) {
    $ahk2Exe = 'C:\Program Files\AutoHotkey\Compiler\Ahk2Exe.exe'
}
if (-not $ahkBase -and (Test-Path 'C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe')) {
    $ahkBase = 'C:\Program Files\AutoHotkey\v2\AutoHotkey64.exe'
}
if (-not $iscc -and (Test-Path 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')) {
    $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
}
$icon = Join-Path $repoRoot 'dist\icon.ico'

foreach ($tool in @($dotnet, $ahk2Exe, $ahkBase, $iscc)) {
    if (-not $tool) { throw "Required build tool not found. See README requirements." }
}

# ---------------------------------------------------------------------------
# Ahk2Exe wrapper: dismiss the benign "Base file appears to be invalid" warning
# that Ahk2Exe 1.1.37 raises against AHK v2 base files. Any real error dialog
# is treated as a hard failure.
# ---------------------------------------------------------------------------
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class HawAhk2Exe {
    [DllImport("user32.dll")]
    public static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cl, string w);
    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
"@

function Invoke-Ahk2Exe {
    param(
        [Parameter(Mandatory)] [string] $InScript,
        [Parameter(Mandatory)] [string] $OutExe,
        [Parameter(Mandatory)] [string] $BaseExe
    )
    $args = @('/in', "`"$InScript`"", '/out', "`"$OutExe`"", '/base', "`"$BaseExe`"")
    if (Test-Path $icon) { $args += @('/icon', "`"$icon`"") }

    $proc = Start-Process -FilePath $ahk2Exe -ArgumentList $args -PassThru
    for ($i = 0; $i -lt 90; $i++) {
        if ($proc.HasExited) { break }
        Start-Sleep -Milliseconds 500
        $proc.Refresh()
        if ($proc.MainWindowTitle -like '*Ahk2Exe Warning*') {
            $btn = [HawAhk2Exe]::FindWindowEx($proc.MainWindowHandle, [IntPtr]::Zero, 'Button', 'OK')
            if ($btn -ne [IntPtr]::Zero) {
                [HawAhk2Exe]::SendMessage($btn, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            }
        } elseif ($proc.MainWindowTitle -like '*Ahk2Exe Error*') {
            $proc | Stop-Process -Force
            throw "Ahk2Exe error while compiling $InScript"
        }
    }
    if (-not $proc.WaitForExit(60000)) {
        $proc | Stop-Process -Force
        throw "Ahk2Exe timed out compiling $InScript"
    }
    if ($proc.ExitCode -ne 0) { throw "Ahk2Exe exited $($proc.ExitCode) for $InScript" }
    if (-not (Test-Path $OutExe)) { throw "Ahk2Exe produced no output at $OutExe" }
}

# ---------------------------------------------------------------------------
# Clean staging
# ---------------------------------------------------------------------------
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path "$staging\service" -Force | Out-Null

# ---------------------------------------------------------------------------
# 1/3 Compile service
# ---------------------------------------------------------------------------
Write-Host '[1/3] Compiling service...'
$serviceScript = Join-Path $repoRoot 'service\main.ahk'
$serviceExe    = Join-Path $staging 'service\HideAnyWindowService.exe'
Invoke-Ahk2Exe -InScript $serviceScript -OutExe $serviceExe -BaseExe $ahkBase
$serviceKb = [math]::Round((Get-Item $serviceExe).Length / 1KB)
Write-Host "       -> $serviceExe (${serviceKb} KB)"

# ---------------------------------------------------------------------------
# 2/3 Publish manager (Release, self-contained)
# ---------------------------------------------------------------------------
Write-Host '[2/3] Publishing manager...'
$managerCsproj = Join-Path $repoRoot 'manager\src\HideAnyWindowManager\HideAnyWindowManager.csproj'
$managerOut    = Join-Path $staging 'manager'
$assets        = Join-Path $repoRoot 'manager\src\HideAnyWindowManager\obj\project.assets.json'
if (-not (Test-Path $assets)) {
    Write-Host '       First run: restoring NuGet packages...'
    & $dotnet restore $managerCsproj -r win-x64 -p:PublishReadyToRun=true `
        -p:RestoreSources=https://api.nuget.org/v3/index.json --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit $LASTEXITCODE" }
}
& $dotnet publish $managerCsproj -c Release -r win-x64 -o $managerOut --nologo --verbosity minimal --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit $LASTEXITCODE" }

$managerExe = Join-Path $managerOut 'HideAnyWindowManager.exe'
if (-not (Test-Path $managerExe)) { throw "Manager exe not produced at $managerExe" }

# WinAppSDK self-contained publish sometimes drops compiled XAML (.xbf) and the
# resource index (.pri). Without them the app crashes on the first XAML page.
$binRel = Join-Path $repoRoot 'manager\src\HideAnyWindowManager\bin\Release\net8.0-windows10.0.26100.0\win-x64'
if (Test-Path $binRel) {
    $copied = 0
    Get-ChildItem $binRel -Filter '*.xbf' -ErrorAction SilentlyContinue | ForEach-Object {
        Copy-Item $_.FullName -Destination $managerOut -Force; $copied++
    }
    Get-ChildItem $binRel -Filter 'HideAnyWindowManager.pri' -ErrorAction SilentlyContinue | ForEach-Object {
        Copy-Item $_.FullName -Destination $managerOut -Force; $copied++
    }
    Write-Host "       Restored $copied XAML/PRI artifact(s)"
}
$managerMb = [math]::Round((Get-ChildItem $managerOut -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "       -> $managerOut (${managerMb} MB)"

# ---------------------------------------------------------------------------
# 3/3 Installer
# ---------------------------------------------------------------------------
Write-Host '[3/3] Building installer...'
$iss = Join-Path $repoRoot 'dist\installer\HideAnyAppsSetup.iss'
& $iscc /Q $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit $LASTEXITCODE" }

$setupExe = Join-Path $repoRoot 'dist\HideAnyApps-Setup.exe'
if (-not (Test-Path $setupExe)) { throw "Setup exe not produced at $setupExe" }
$setupMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host ''
Write-Host "Done: $setupExe ($setupMb MB)" -ForegroundColor Green
