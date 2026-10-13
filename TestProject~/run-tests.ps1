<#
.SYNOPSIS
  HeroSignal Unity paketini gerçek Unity Editor'de derler ve EditMode testlerini çalıştırır.

.DESCRIPTION
  Paket `TestProject~` projesine yerel yol ile eklenir (`Packages/manifest.json`). Testler her derleme hedefinde ayrı
  koşturulur; böylece `UNITY_ANDROID` / `UNITY_IOS` koşullu kodlar (AndroidBridge, IosBridge, Gradle/Xcode
  PostProcess betikleri) de derlenmiş olur. Android ve iOS hedefleri, ilgili "Build Support" modülü Editor'e kurulu
  değilse atlanır ve bu açıkça raporlanır.

  Önkoşul: Unity Hub'da oturum açılmış ve lisans (Personal dahil) etkin olmalıdır; aksi halde Unity batchmode'da
  lisans hatasıyla kapanır.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File sdk/unity/TestProject~/run-tests.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File sdk/unity/TestProject~/run-tests.ps1 -UnityPath "D:\Unity\6000.0.40f1\Editor\Unity.exe"
#>
param(
  [string]$UnityPath,
  [string[]]$Targets = @('StandaloneWindows64', 'Android', 'iOS')
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1'de `Set-Content -Encoding utf8` BOM yazar; Unity Package Manager BOM'lu manifest.json'u
# geçersiz JSON sayıp hiçbir paketi yüklemez. Bu yüzden JSON dosyaları BOM'suz UTF-8 ile yazılır.
function Write-Utf8NoBom([string]$Path, [string]$Text) {
  [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding $false))
}
$template = $PSScriptRoot
$results = Join-Path $template 'TestResults'
New-Item -ItemType Directory -Force -Path $results | Out-Null

# Unity, adında `~` geçen proje yolunu göreli sayıp çalışma klasörüyle birleştirir; ayrıca Library önbelleği birkaç
# yüz MB'tır. Bu yüzden proje depo dışında (geçici klasörde) kurulur ve pakete mutlak `file:` yoluyla bağlanır.
$packageRoot = (Resolve-Path (Join-Path $template '..')).Path -replace '\\', '/'
$project = Join-Path ([System.IO.Path]::GetTempPath()) 'herosignal-unity-testproject'
# Unity `Assets` ve `ProjectSettings` olmayan dizini proje saymaz; boş `ProjectSettings` ile ilk açılışta varsayılan
# şablon paketlerini ekler. Minimal manifest her koşuda yeniden yazılır (ağdan gereksiz paket çekilmez).
foreach ($dir in 'Packages', 'Assets', 'ProjectSettings') {
  New-Item -ItemType Directory -Force -Path (Join-Path $project $dir) | Out-Null
}
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $project 'Packages\packages-lock.json')
$manifest = Get-Content (Join-Path $template 'Packages\manifest.json') -Raw | ConvertFrom-Json
$manifest.dependencies.'com.herosignal.unity' = "file:$packageRoot"
Write-Utf8NoBom (Join-Path $project 'Packages\manifest.json') ($manifest | ConvertTo-Json -Depth 5)
Write-Host "Proje: $project"

if (-not $UnityPath) {
  $roots = @("$env:ProgramFiles\Unity\Hub\Editor")
  $secondary = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
  if (Test-Path $secondary) {
    $custom = (Get-Content $secondary -Raw | ConvertFrom-Json)
    if ($custom) { $roots += $custom }
  }
  $candidates = foreach ($r in $roots) {
    if (Test-Path $r) { Get-ChildItem $r -Directory | ForEach-Object { Join-Path $_.FullName 'Editor\Unity.exe' } | Where-Object { Test-Path $_ } }
  }
  $UnityPath = $candidates | Sort-Object -Descending | Select-Object -First 1
}
if (-not $UnityPath -or -not (Test-Path $UnityPath)) {
  throw 'Unity Editor bulunamadı. Unity Hub ile bir Editor kurun veya -UnityPath verin.'
}
$editorRoot = Split-Path $UnityPath -Parent
Write-Host "Unity: $UnityPath"

# Sürüm dosyası yoksa Unity projeyi "yeni" sayar ve manifesti varsayılan şablon paketleriyle (ağdan indirilen
# analytics, purchasing…) değiştirir. Editor sürümü kurulum klasörünün adıdır (Hub düzeni: <sürüm>\Editor\Unity.exe).
$editorVersion = Split-Path (Split-Path $editorRoot -Parent) -Leaf
Set-Content -Path (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Value "m_EditorVersion: $editorVersion" -Encoding ascii
Write-Utf8NoBom (Join-Path $project 'Packages\manifest.json') ($manifest | ConvertTo-Json -Depth 5)

# Hedefe göre gerekli PlaybackEngines modülü.
$modules = @{ 'Android' = 'AndroidPlayer'; 'iOS' = 'iOSSupport'; 'StandaloneWindows64' = 'WindowsStandaloneSupport' }
$failed = $false
$summary = @()

foreach ($target in $Targets) {
  $module = Join-Path $editorRoot "Data\PlaybackEngines\$($modules[$target])"
  if ($target -ne 'StandaloneWindows64' -and -not (Test-Path $module)) {
    $summary += "{0,-20} ATLANDI ({1} Build Support modülü kurulu değil)" -f $target, $target
    continue
  }
  $xml = Join-Path $results "editmode-$target.xml"
  $log = Join-Path $results "unity-$target.log"
  if (Test-Path $xml) { Remove-Item $xml }
  Write-Host "→ $($target): derleme + EditMode testleri…"
  $unityArgs = @('-batchmode', '-nographics', '-projectPath', $project, '-buildTarget', $target,
            '-runTests', '-testPlatform', 'EditMode', '-testResults', $xml, '-logFile', $log)
  $proc = Start-Process -FilePath $UnityPath -ArgumentList $unityArgs -Wait -PassThru -NoNewWindow
  if (-not (Test-Path $xml)) {
    $failed = $true
    $errors = Select-String -Path $log -Pattern 'error CS\d+|License|licen[cs]e' -ErrorAction SilentlyContinue | Select-Object -First 8
    $summary += "{0,-20} BAŞARISIZ (çıkış {1}; sonuç dosyası yok, log: {2})" -f $target, $proc.ExitCode, $log
    if ($errors) { $summary += ($errors | ForEach-Object { '    ' + $_.Line.Trim() }) }
    continue
  }
  [xml]$doc = Get-Content $xml
  $run = $doc.'test-run'
  $line = "{0,-20} toplam {1}, geçen {2}, başarısız {3}, atlanan {4}" -f $target, $run.total, $run.passed, $run.failed, $run.skipped
  if ([int]$run.failed -gt 0) { $failed = $true; $line += '  ✘' } else { $line += '  ✔' }
  $summary += $line
}

Write-Host ''
Write-Host 'Sonuç:'
$summary | ForEach-Object { Write-Host "  $_" }
if ($failed) { exit 1 }
