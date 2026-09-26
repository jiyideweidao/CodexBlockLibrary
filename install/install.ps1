# 安装 CodexBlockLibrary 到 AutoCAD 2024（用户级 ApplicationPlugins，无需管理员权限）
param(
  [switch]$AllUsers,   # 安装到所有用户（需要管理员权限）
  [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$bundle  = Join-Path $root 'bundle\CodexBlockLibrary.bundle'
$dist    = Join-Path $root 'dist\Contents\Windows'

if (-not $SkipBuild) { & (Join-Path $root 'build\build.ps1') }

# 1) 把编译产物放入 bundle 负载目录
$payload = Join-Path $bundle 'Contents\Windows'
New-Item -ItemType Directory -Force -Path $payload | Out-Null
Copy-Item (Join-Path $dist '*.dll') $payload -Force
Get-ChildItem $payload -Filter *.dll | ForEach-Object { Write-Host "  负载: $($_.Name)" }

# 2) 复制 bundle 到 AutoCAD 插件自动加载目录
if ($AllUsers) {
  $target = Join-Path $env:PROGRAMDATA 'Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle'
} else {
  $target = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle'
}
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $bundle '*') $target -Recurse -Force

Write-Host ""
Write-Host "已安装到: $target" -ForegroundColor Green
Get-ChildItem $target -Recurse -File | ForEach-Object { Write-Host ("  " + $_.FullName.Substring($target.Length + 1) + "  (" + $_.Length + " 字节)") }

Write-Host ""
Write-Host "下一步: 重启 AutoCAD 2024 使 bundle 自动加载。" -ForegroundColor Yellow
Write-Host "  或在不重启的情况下，于 AutoCAD 命令行执行: NETLOAD -> 选择 UI DLL"
Write-Host ("  " + (Join-Path $target 'Contents\Windows\CodexBlockLib.UI.dll'))