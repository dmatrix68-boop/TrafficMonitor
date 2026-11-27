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

# Sprachdateien (außer Englisch/Chinesisch nicht in die exe eingebettet) in den Ordner "language" neben der exe kopieren
$langDir = Join-Path $exeDir "language"
New-Item -ItemType Directory -Force -Path $langDir | Out-Null
Copy-Item -Path (Join-Path $repo "TrafficMonitor\language\*.ini") -Destination $langDir -Force

# Sprach-ID für die Konfiguration ermitteln: "BCP_47|DISPLAY_NAME|TRANSLATOR" aus der passenden Sprachdatei
$languageValue = $Language
$expectedTitle = ""
foreach ($f in Get-ChildItem $langDir -Filter *.ini) {
    $content = Get-Content $f.FullName -Encoding UTF8
    $bcp = ($content | Select-String '^\s*BCP_47\s*=\s*"(.*)"').Matches | Select-Object -First 1
    if ($bcp -and $bcp.Groups[1].Value -eq $Language) {
        $name = (($content | Select-String '^\s*DISPLAY_NAME\s*=\s*"(.*)"').Matches | Select-Object -First 1).Groups[1].Value
        $translator = (($content | Select-String '^\s*TRANSLATOR\s*=\s*"(.*)"').Matches | Select-Object -First 1).Groups[1].Value
        $languageValue = "$Language|$name|$translator"
        $expectedTitle = (($content | Select-String '^\s*TXT_TITLE_OPTION\s*=\s*"(.*)"').Matches | Select-Object -First 1).Groups[1].Value
        Write-Host "Sprachdatei: $($f.Name) -> $languageValue"
    }
}

# Portable Konfiguration neben der exe anlegen (UTF-8 mit BOM, wie von CIniHelper erwartet): Sprache setzen, Update-Prüfung und Taskleistenfenster aus,
# langer Schriftname für das Taskleistenfenster (prüft, ob Eingabefelder den Text ab dem Anfang zeigen)
Set-Content -Path (Join-Path $exeDir "global_cfg.ini") -Encoding UTF8 -Value "[config]`r`nportable_mode = true`r`n"
Set-Content -Path (Join-Path $exeDir "config.ini") -Encoding UTF8 -Value @"
[general]
language = $languageValue
check_update_when_start = false
hardware_monitor_item = 0
[task_bar]
font_name = Segoe UI Variable Display Semibold
[config]
show_task_bar_wnd = false
show_notify_icon = true
"@

Add-Type -Path (Join-Path $PSScriptRoot "DialogLayoutCheck.cs") -ReferencedAssemblies System.Drawing

$rc = [DialogLayoutCheck]::Run($exe, (Join-Path $repo "TrafficMonitor\resource.h"), $outDir, $FontName, $expectedTitle)
Write-Host "Exit-Code der Layout-Prüfung: $rc"
exit $rc
