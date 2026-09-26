<#
  Codex 图块库（CodexBlockLibrary）卸载脚本 - 分发版
  作用：删除 ApplicationPlugins 里的插件包，可选删除用户配置与缓存。

  用法：
    uninstall.ps1              卸载（会询问是否删除用户配置）
    uninstall.ps1 -KeepConfig  卸载但保留用户配置
    uninstall.ps1 -PurgeConfig 卸载并直接删除用户配置（不再询问）
    uninstall.ps1 -AllUsers    同时清理由管理员安装的“所有用户”副本（会请求提权）
    uninstall.ps1 -DryRun      只显示将要删除的内容
#>
param(
    [switch]$KeepConfig,
    [switch]$PurgeConfig,
    [switch]$AllUsers,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$BundleName = 'CodexBlockLibrary.bundle'

function Head([string]$t) { Write-Host ''; Write-Host ('== ' + $t + ' ==') -ForegroundColor Cyan }
function Ok([string]$t)   { Write-Host ('  [OK] ' + $t) -ForegroundColor Green }
function Info([string]$t) { Write-Host ('  ' + $t) }
function Warn([string]$t) { Write-Host ('  [!] ' + $t) -ForegroundColor Yellow }
function Fail([string]$t) { Write-Host ('  [X] ' + $t) -ForegroundColor Red }

Write-Host ''
Write-Host '  Codex 图块库 - AutoCAD 插件卸载程序' -ForegroundColor White
Write-Host '  -----------------------------------' -ForegroundColor DarkGray

$isAdmin = $false
try {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} catch { $isAdmin = $false }

$userTarget    = Join-Path (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins') $BundleName
$machineTarget = Join-Path (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins') $BundleName
$configDir     = Join-Path (Join-Path $env:APPDATA 'Autodesk') 'CodexBlockLib'

$targets = New-Object System.Collections.Generic.List[string]
if (Test-Path -LiteralPath $userTarget) { $targets.Add($userTarget) }
if (Test-Path -LiteralPath $machineTarget) { $targets.Add($machineTarget) }

if ($targets.Count -eq 0) {
    Warn '没有找到已安装的 Codex 图块库（两个 ApplicationPlugins 目录下都不存在插件包）。'
    if (Test-Path -LiteralPath $configDir) { Info ('用户配置仍在：' + $configDir) }
    exit 0
}

# AutoCAD 运行中则无法删除被占用的 dll
$acadRunning = $false
try { if (Get-Process -Name acad -ErrorAction SilentlyContinue) { $acadRunning = $true } } catch { }

Head '将要删除'
foreach ($t in $targets) { Info ('插件目录 ：' + $t) }
$delConfig = $false
if ((-not $KeepConfig) -and (Test-Path -LiteralPath $configDir)) {
    if ($PurgeConfig) { $delConfig = $true }
    elseif ($DryRun) { $delConfig = $true; Info ('用户配置 ：' + $configDir + '（正式运行时将询问）') }
    else {
        Write-Host ''
        Info ('用户配置目录：' + $configDir)
        Info '（包含：已登记源图纸、自定义分类与标签、缩略图缓存、日志）'
        $a = Read-Host '  是否一并删除？[y/N]'
        if ($a -and ($a.Trim().ToLower() -eq 'y' -or $a.Trim().ToLower() -eq 'yes')) { $delConfig = $true }
    }
    if ($delConfig -and -not $DryRun) { Info ('用户配置 ：' + $configDir + '（将删除）') }
}

if ($acadRunning) {
    Fail 'AutoCAD 正在运行，插件 dll 被占用，无法删除。'
    Info '请先关闭 AutoCAD，然后重新运行本卸载程序。'
    exit 4
}

if ($DryRun) { Head '试运行模式：未删除任何文件'; exit 0 }

Head '开始卸载'
$failed = 0
foreach ($t in $targets) {
    if ($t -ieq $machineTarget -and -not $isAdmin) {
        Warn ('跳过（需要管理员权限）：' + $t)
        $failed++
        continue
    }
    try {
        [System.IO.Directory]::Delete($t, $true)
        Ok ('已删除 ' + $t)
    } catch {
        Fail ('删除失败：' + $t + ' :: ' + $_.Exception.Message)
        $failed++
    }
}

if ($delConfig) {
    try {
        [System.IO.Directory]::Delete($configDir, $true)
        Ok ('已删除用户配置 ' + $configDir)
    } catch {
        Warn ('用户配置删除失败（可稍后手动删除）：' + $_.Exception.Message)
    }
}

Write-Host ''
if ($failed -eq 0) {
    Head '卸载完成'
    Ok '插件已移除，下次启动 AutoCAD 不会再加载。'
    Info '若 AutoCAD 当前是打开的，建议重启一次以彻底释放已加载的 dll。'
} else {
    Head '卸载未完全成功'
    Warn ('有 ' + $failed + ' 个位置未能删除，请关闭 AutoCAD 后重试，或手动删除上面列出的目录。')
    exit 5
}
exit 0