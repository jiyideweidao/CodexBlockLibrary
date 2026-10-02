<#
  Codex 图块库 - 打包脚本
  产出（release 目录）：
    CodexBlockLibrary-1.0.7-Setup.exe   单文件 GUI 安装程序（内嵌插件包）
    CodexBlockLibrary-1.0.7.zip         便携包（bundle + 安装/卸载脚本 + 文档）
    SHA256SUMS.txt                       校验值

  用法：  & .\package\make-package.ps1               （先编译再打包）
          & .\package\make-package.ps1 -SkipBuild   （拿现有 dist 打包）
#>
param(
  [switch]$SkipBuild,
  [string]$Version    = '1.0.7',
  [string]$ToolsRoot,
  [string]$Csc,
  [string]$Refs,
  [string]$AcadDir,
  [string]$SevenZip
)
$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'build\toolchain.ps1')
if (-not $ToolsRoot) { $ToolsRoot = Join-Path (Split-Path -Parent $root) '_buildtools' }
$tc = Resolve-CodexToolchain -ToolsRoot $ToolsRoot -Csc $Csc -Refs $Refs -AcadDir $AcadDir -SevenZip $SevenZip
$csc      = $tc.Csc
$refs     = $tc.Refs
$sevenZip = $tc.SevenZip
if (-not $csc)  { throw '未找到 Roslyn csc.exe，无法编译 Setup.exe（可设 CODEX_CSC 或 -Csc）' }
if (-not $refs) { throw '未找到 .NET Framework 4.8 参考程序集（可设 CODEX_REFS 或 -Refs）' }
foreach ($n in $tc.Notes) { Write-Host ('   [提示] ' + $n) -ForegroundColor DarkYellow }

$bundleSrc = Join-Path $root 'bundle\CodexBlockLibrary.bundle'
$installer = Join-Path $root 'package\installer'
$setupSrc  = Join-Path $root 'package\setup'
$release   = Join-Path $root 'release'
$name      = 'CodexBlockLibrary-' + $Version
$stageRoot = Join-Path $release 'stage'
$stage     = Join-Path $stageRoot $name
$work      = Join-Path $release 'work'

function Step([string]$t) { Write-Host ''; Write-Host ('== ' + $t + ' ==') -ForegroundColor Cyan }

# ---------- 1) 编译插件 ----------
if (-not $SkipBuild) {
  Step '编译插件（Core + UI）'
  $bp = @{}
  if ($ToolsRoot) { $bp.ToolsRoot = $ToolsRoot }
  if ($Csc)       { $bp.Csc       = $Csc }
  if ($Refs)      { $bp.Refs      = $Refs }
  if ($AcadDir)   { $bp.AcadDir   = $AcadDir }
  & (Join-Path $root 'build\build.ps1') @bp
}

$distDll    = Join-Path $root 'dist\Contents\Windows'
$payloadDir = Join-Path $bundleSrc 'Contents\Windows'
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null

if (Test-Path (Join-Path $distDll 'CodexBlockLib.UI.dll')) {
  Step '同步编译产物到 bundle'
  Copy-Item (Join-Path $distDll '*.dll') $payloadDir -Force
} else {
  Write-Host ''
  Write-Host '== 跳过同步（本次未编译，使用 bundle 内已有 DLL）==' -ForegroundColor DarkYellow
}

if (-not (Test-Path (Join-Path $payloadDir 'CodexBlockLib.Core.dll'))) {
  throw '找不到插件 DLL：既没有 dist 编译产物，bundle\Contents\Windows 下也没有现成的 dll'
}
Get-ChildItem $payloadDir -Filter *.dll | ForEach-Object { Write-Host ('   ' + $_.Name + '  ' + $_.Length + ' 字节') }

# ---------- 2) 准备 staging ----------
Step '准备打包目录'
foreach ($d in @($stageRoot, $work)) { if (Test-Path $d) { [System.IO.Directory]::Delete($d, $true) } }
New-Item -ItemType Directory -Force -Path $stage, $work | Out-Null

Copy-Item -LiteralPath $bundleSrc -Destination (Join-Path (Join-Path $stage 'bundle') 'CodexBlockLibrary.bundle') -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'bundle') | Out-Null
Get-ChildItem -LiteralPath $installer -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $stage -Force }
Copy-Item -LiteralPath (Join-Path $installer 'tools') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $stage 'README.md') -Force

$docs = Join-Path $stage 'docs'
New-Item -ItemType Directory -Force -Path $docs | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $docs '使用说明与实现说明.md') -Force
$research = Join-Path $root 'docs\GitHub同类项目调研.md'
if (Test-Path -LiteralPath $research) { Copy-Item -LiteralPath $research -Destination (Join-Path $docs 'GitHub同类项目调研.md') -Force }

# ---------- 3) 生成 Setup.exe 用的内嵌负载 ----------
Step '生成内嵌负载 payload.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadZip = Join-Path $work 'payload.zip'
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $bundleSrc, $payloadZip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ('   payload.zip = ' + (Get-Item $payloadZip).Length + ' 字节')

# ---------- 4) 编译单文件安装程序 ----------
Step '编译 Setup.exe（内嵌 payload.zip 与 README）'
$setupExe = Join-Path $release ($name + '-Setup.exe')
$opt = New-Object 'System.Collections.Generic.List[string]'
$opt.AddRange([string[]]@('/nologo','/target:winexe','/platform:AnyCPU','/langversion:7.3','/codepage:65001','/nostdlib+','/optimize+','/warn:4'))
$opt.Add('/out:' + $setupExe)
foreach ($r in @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')) {
  $opt.Add('/r:' + (Join-Path $refs $r))
}
$opt.Add('/resource:' + $payloadZip + ',CodexBlockLibrary.payload.zip')
$opt.Add('/resource:' + (Join-Path $root 'README.md') + ',CodexBlockLibrary.readme.md')
$opt.Add((Join-Path $setupSrc 'Setup.cs'))
$opt.Add((Join-Path $setupSrc 'AssemblyInfo.cs'))
$cscOut = & $csc $opt.ToArray() 2>&1
$cscOut | ForEach-Object { Write-Host ('   ' + $_.ToString()) }
if ($LASTEXITCODE -ne 0) { throw ('Setup.exe 编译失败 (exit ' + $LASTEXITCODE + ')') }
Write-Host ('   ' + $setupExe + '  ' + (Get-Item $setupExe).Length + ' 字节') -ForegroundColor Green

# ---------- 5) 生成便携 zip ----------
# 把单文件安装程序也放进便携包，方便只下载一个压缩包也能一键安装
Copy-Item -LiteralPath $setupExe -Destination (Join-Path $stage 'Setup.exe') -Force

Step '生成便携 zip'
$zipPath = Join-Path $release ($name + '.zip')
if (Test-Path $zipPath) { [System.IO.File]::Delete($zipPath) }
Push-Location $stageRoot
if ($sevenZip) {
  & $sevenZip a -tzip -mx=9 -mcu=on $zipPath $name | Out-Null
} else {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [System.IO.Compression.ZipFile]::CreateFromDirectory(
    (Join-Path $stageRoot $name), $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal, $true)
}
Pop-Location
if (-not (Test-Path $zipPath)) { throw 'zip 生成失败' }
Write-Host ('   ' + $zipPath + '  ' + (Get-Item $zipPath).Length + ' 字节') -ForegroundColor Green

# ---------- 6) 校验值 ----------
Step '生成 SHA256SUMS.txt'
$sums = Join-Path $release 'SHA256SUMS.txt'
$lines = New-Object System.Collections.Generic.List[string]
foreach ($f in @($setupExe, $zipPath)) {
  $h = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash
  $lines.Add($h + '  ' + (Split-Path -Leaf $f))
}
[System.IO.File]::WriteAllLines($sums, $lines)
$lines | ForEach-Object { Write-Host ('   ' + $_) }

Step '打包完成'
Get-ChildItem $release -File | Sort-Object Name | ForEach-Object {
  Write-Host ('   ' + $_.Name.PadRight(38) + [string]([int]($_.Length / 1KB)) + ' KB')
}
Write-Host ''
Write-Host ('   staging: ' + $stage) -ForegroundColor DarkGray
