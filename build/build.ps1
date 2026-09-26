# CodexBlockLibrary 构建脚本（使用 Roslyn csc + .NET Framework 4.8 参考程序集，无需 VS/MSBuild）
# 路径自动探测，可用参数或环境变量覆盖：-Csc / -Refs / -AcadDir / -ToolsRoot（CODEX_CSC / CODEX_REFS / ACAD_DIR）
param(
  [switch]$CoreOnly,
  [string]$ToolsRoot,
  [string]$Csc,
  [string]$Refs,
  [string]$AcadDir
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot 'toolchain.ps1')
if (-not $ToolsRoot) { $ToolsRoot = Join-Path (Split-Path -Parent $root) '_buildtools' }
$tc = Resolve-CodexToolchain -ToolsRoot $ToolsRoot -Csc $Csc -Refs $Refs -AcadDir $AcadDir
if (-not $tc.Csc) { throw ('未找到 Roslyn csc.exe。' + ($tc.Notes -join ' ')) }
if (-not $tc.Refs) { throw ('.NET Framework 4.8 参考程序集缺失，可设置 CODEX_REFS。' + ($tc.Notes -join ' ')) }
if (-not $tc.AcadDir) {
  throw ('未找到 AutoCAD 2024 托管程序集（需要 acdbmgd.dll / accoremgd.dll / acmgd.dll / AdWindows.dll）。' +
         '请安装 AutoCAD 2024，或设置环境变量 ACAD_DIR 指向安装目录，或把它们放到 _buildtools\acad2024\。')
}
$csc = $tc.Csc
$refs = $tc.Refs
$cad = $tc.AcadDir
$out = Join-Path $root 'dist\Contents\Windows'
New-Item -ItemType Directory -Force -Path $out | Out-Null
Write-Host "== 工具链 ==" -ForegroundColor DarkGray
Write-Host ("   csc  : " + $csc) -ForegroundColor DarkGray
Write-Host ("   refs : " + $refs) -ForegroundColor DarkGray
Write-Host ("   acad : " + $cad) -ForegroundColor DarkGray

function Build-Lib {
  param([string]$Name, [string[]]$Sources, [string[]]$ExtraRefs, [string[]]$ExtraOpts)
  $opt = New-Object 'System.Collections.Generic.List[string]'
  $opt.AddRange([string[]]@('/nologo','/target:library','/platform:AnyCPU','/langversion:7.3','/codepage:65001','/nostdlib+','/optimize+','/warn:3'))
  $opt.Add('/out:' + (Join-Path $out "$Name.dll"))
  $opt.AddRange([string[]]$ExtraOpts)
  foreach ($r in @('mscorlib.dll','System.dll','System.Core.dll','System.Xml.dll')) { $opt.Add("/r:$(Join-Path $refs $r)") }
  foreach ($r in $ExtraRefs) { $opt.Add("/r:$r") }
  foreach ($f in $Sources) { $opt.Add($f) }
  Write-Host "== 编译 $Name ==" -ForegroundColor Cyan
  $cscOut = & $csc $opt.ToArray() 2>&1
  $cscOut | ForEach-Object { Write-Host $_.ToString() }
  if ($LASTEXITCODE -ne 0) { throw "$Name 编译失败 (exit $LASTEXITCODE)" }
  Write-Host "   -> $(Join-Path $out "$Name.dll")" -ForegroundColor Green
}

$coreSrc = (Get-ChildItem (Join-Path $root 'src\Core') -Filter *.cs | ForEach-Object { $_.FullName })
Build-Lib -Name 'CodexBlockLib.Core' -Sources $coreSrc -ExtraRefs @(
  (Join-Path $refs 'System.Drawing.dll'),
  (Join-Path $cad 'acdbmgd.dll'),
  (Join-Path $cad 'accoremgd.dll')
) -ExtraOpts @()

if ($CoreOnly) { return }

$uiSrc = (Get-ChildItem (Join-Path $root 'src\UI') -Filter *.cs | ForEach-Object { $_.FullName })
Build-Lib -Name 'CodexBlockLib.UI' -Sources $uiSrc -ExtraRefs @(
  (Join-Path $refs 'System.Drawing.dll'),
  (Join-Path $refs 'System.Windows.Forms.dll'),
  (Join-Path $refs 'WindowsBase.dll'),
  (Join-Path $refs 'PresentationCore.dll'),
  (Join-Path $refs 'PresentationFramework.dll'),
  (Join-Path $refs 'System.Xaml.dll'),
  (Join-Path $cad 'acdbmgd.dll'),
  (Join-Path $cad 'accoremgd.dll'),
  (Join-Path $cad 'acmgd.dll'),
  (Join-Path $cad 'AdWindows.dll'),
  (Join-Path $out 'CodexBlockLib.Core.dll')
) -ExtraOpts @()