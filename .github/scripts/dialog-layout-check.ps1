# Prüft das Layout des Einstellungsdialogs von TrafficMonitor in einer bestimmten Sprache.
# Aufruf: powershell -File dialog-layout-check.ps1 -ExeDir "Bin\x64\Release (lite)" -Language de-DE -OutDir layout-check
param(
    [Parameter(Mandatory = $true)][string]$ExeDir,
    [string]$Language = "de-DE",
    [string]$OutDir = "layout-check",
    [string]$FontName = "Segoe UI"
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$exeDir = Resolve-Path (Join-Path $repo $ExeDir)
$exe = Join-Path $exeDir "TrafficMonitor.exe"
$outDir = Join-Path $repo $OutDir
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Portable Konfiguration neben der exe anlegen (UTF-8 mit BOM, wie von CIniHelper erwartet): Sprache setzen, Update-Prüfung und Taskleistenfenster aus
Set-Content -Path (Join-Path $exeDir "global_cfg.ini") -Encoding UTF8 -Value "[config]`r`nportable_mode = true`r`n"
Set-Content -Path (Join-Path $exeDir "config.ini") -Encoding UTF8 -Value @"
[general]
language = $Language
check_update_when_start = false
hardware_monitor_item = 0
[config]
show_task_bar_wnd = false
show_notify_icon = true
"@

Add-Type -Path (Join-Path $PSScriptRoot "DialogLayoutCheck.cs") -ReferencedAssemblies System.Drawing

$rc = [DialogLayoutCheck]::Run($exe, (Join-Path $repo "TrafficMonitor\resource.h"), $outDir, $FontName)
Write-Host "Exit-Code der Layout-Prüfung: $rc"
exit $rc
