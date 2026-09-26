<#
  Codex 图块库（CodexBlockLibrary）安装脚本 - 分发版
  作用：把插件包复制到 AutoCAD 的 ApplicationPlugins 自动加载目录，无需编译。

  用法：
    install.ps1               安装到当前用户（不需要管理员权限）
    install.ps1 -AllUsers     安装到所有用户（需要管理员，会自动请求提权）
    install.ps1 -DryRun       只显示将要执行的操作，不复制任何文件
    install.ps1 -Force        即使 AutoCAD 正在运行也继续（可能无法覆盖旧文件）
#>
param(
    [switch]$AllUsers,
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$BundleName = 'CodexBlockLibrary.bundle'

function Head([string]$t) { Write-Host ''; Write-Host ('== ' + $t + ' ==') -ForegroundColor Cyan }
function Ok([string]$t)   { Write-Host ('  [OK] ' + $t) -ForegroundColor Green }
function Info([string]$t) { Write-Host ('  ' + $t) }
function Warn([string]$t) { Write-Host ('  [!] ' + $t) -ForegroundColor Yellow }
function Fail([string]$t) { Write-Host ('  [X] ' + $t) -ForegroundColor Red }

$packageRoot  = Split-Path -Parent $PSScriptRoot
$sourceBundle = Join-Path (Join-Path $packageRoot 'bundle') $BundleName

Write-Host ''
Write-Host '  Codex 图块库 - AutoCAD 插件安装程序' -ForegroundColor White
Write-Host '  -----------------------------------' -ForegroundColor DarkGray

# 1) 校验安装包完整
if (-not (Test-Path -LiteralPath (Join-Path $sourceBundle 'PackageContents.xml'))) {
    Fail ('安装包不完整，找不到：' + (Join-Path $sourceBundle 'PackageContents.xml'))
    Info '请重新下载完整的安装包、完整解压后再运行。'
    exit 2
}

# 2) 管理员判定 / 自动提权
$isAdmin = $false
try {
    $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} catch { $isAdmin = $false }

if ($AllUsers -and (-not $isAdmin) -and (-not $DryRun)) {
    Head '需要管理员权限，正在请求提权'
    try {
        $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'), '-AllUsers')
        Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs | Out-Null
        Info '已在新的管理员窗口中继续安装。'
    } catch {
        Fail ('提权失败：' + $_.Exception.Message)
        Info '也可以右键 install-allusers.cmd，选择 [以管理员身份运行]。'
        exit 3
    }
    exit 0
}

# 3) 目标目录
if ($AllUsers) {
    $target = Join-Path (Join-Path $env:ProgramData 'Autodesk\ApplicationPlugins') $BundleName
    $scope  = '所有用户（需要管理员权限）'
} else {
    $target = Join-Path (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins') $BundleName
    $scope  = '当前用户（' + $env:USERNAME + '）'
}

# 4) AutoCAD 是否在运行
$acadRunning = $false
try { if (Get-Process -Name acad -ErrorAction SilentlyContinue) { $acadRunning = $true } } catch { }
$targetExists = Test-Path -LiteralPath $target

Head '安装信息'
Info ('安装范围 ：' + $scope)
Info ('源插件包 ：' + $sourceBundle)
Info ('目标目录 ：' + $target)
Info ('已装版本 ：' + $(if ($targetExists) { '是，将覆盖升级' } else { '无，全新安装' }))

if ($acadRunning) { Warn 'AutoCAD 正在运行：安装完请重启 AutoCAD 才会加载插件。' }

if ($acadRunning -and $targetExists -and (-not $Force) -and (-not $DryRun)) {
    Fail 'AutoCAD 正在运行，旧版本插件的 dll 被占用，无法覆盖。'
    Info '请先关闭 AutoCAD，然后重新运行本安装程序。'
    exit 4
}

if ($DryRun) {
    Head '试运行模式：以上为将要执行的操作，未改动任何文件'
    exit 0
}

# 5) 复制插件包
Head '开始安装'
try {
    $parent = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    if ($targetExists) {
        Info '移除旧版本文件...'
        [System.IO.Directory]::Delete($target, $true)
    }
    Copy-Item -LiteralPath $sourceBundle -Destination $target -Recurse -Force
} catch {
    Fail ('复制失败：' + $_.Exception.Message)
    Info '如果是杀毒软件拦截，请先允许本程序再重试。'
    exit 5
}

# 6) 校验结果
$manifest = Join-Path $target 'PackageContents.xml'
$uiDll    = Join-Path $target 'Contents\Windows\CodexBlockLib.UI.dll'
$coreDll  = Join-Path $target 'Contents\Windows\CodexBlockLib.Core.dll'
if ((-not (Test-Path -LiteralPath $manifest)) -or (-not (Test-Path -LiteralPath $uiDll)) -or (-not (Test-Path -LiteralPath $coreDll))) {
    Fail '安装后校验失败：插件文件不完整。'
    exit 6
}

Head '安装完成'
Ok ('插件已安装到 ' + $target)
Ok '文件校验通过：PackageContents.xml / CodexBlockLib.UI.dll / CodexBlockLib.Core.dll'
Write-Host ''
Get-ChildItem -LiteralPath $target -Recurse -File | ForEach-Object {
    Info ('  ' + $_.FullName.Substring($target.Length + 1) + '  (' + $_.Length + ' 字节)')
}

# 7) 尽力探测本机 AutoCAD（仅作提示）
$acads = @()
try {
    Get-ChildItem 'HKLM:\SOFTWARE\Autodesk\AutoCAD' -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem $_.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
            $loc = (Get-ItemProperty -Path $_.PSPath -Name AcadLocation -ErrorAction SilentlyContinue).AcadLocation
            if ($loc -and (Test-Path -LiteralPath (Join-Path $loc 'acad.exe'))) { $acads += $loc }
        }
    }
} catch { }

Head '下一步'
Info '  1. 启动 AutoCAD（若已启动请重启，bundle 只在启动时自动加载）'
Info '  2. 命令行输入 BLKLIB 打开 [Codex 图块库] 面板，或用菜单栏 [块库(K)]'
Info '  3. 面板默认折叠成窄条，点状态栏 [展开] 可展开，点 [卸载] 可卸载插件'
Info '  卸载：运行 uninstall.cmd；或删除下面这个目录'
Info ('    ' + $target)
if ($acads.Count -gt 0) {
    Write-Host ''
    Info ('本机检测到 AutoCAD：' + ($acads -join ' ; '))
    Warn '插件按 PackageContents.xml 要求 AutoCAD 2024 或更高版本（完整版，不支持 LT）。'
} else {
    Write-Host ''
    Warn '未在注册表中检测到 AutoCAD。本插件需要已安装 AutoCAD 2024 或更高版本（完整版）。'
}
Write-Host ''
Ok '安装脚本执行结束。'
exit 0