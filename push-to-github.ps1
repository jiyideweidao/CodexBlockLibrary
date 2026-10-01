<#
  Codex 图块库 - 一键推到 GitHub
  前置：装好 GitHub CLI（gh）。本机已装：winget install --id GitHub.cli -e --scope user

  用法：
    & .\push-to-github.ps1                      # 建仓库 CodexBlockLibrary 并推送 main
    & .\push-to-github.ps1 -Repo 我的仓库名 -Private
    & .\push-to-github.ps1 -Tag v1.0.6          # 推送后顺便打 tag，触发 CI 自动发版
#>
param(
  [string]$Repo = 'CodexBlockLibrary',
  [switch]$Private,
  [string]$Tag
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Find-Gh {
  $g = (Get-Command gh -ErrorAction SilentlyContinue).Source
  if ($g) { return $g }
  foreach ($p in @("$env:ProgramFiles\GitHub CLI\gh.exe", "$env:LOCALAPPDATA\Microsoft\WindowsApps\gh.exe")) {
    if (Test-Path $p) { return $p }
  }
  $hit = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter gh.exe -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($hit) { return $hit.FullName }
  return $null
}

$gh = Find-Gh
if (-not $gh) {
  Write-Host '[X] 没找到 GitHub CLI（gh）。' -ForegroundColor Red
  Write-Host '    安装：winget install --id GitHub.cli -e --scope user' -ForegroundColor Yellow
  Write-Host '    或者手动：在 github.com 新建空仓库，然后执行' -ForegroundColor Yellow
  Write-Host '      git remote add origin https://github.com/<你的账号>/<仓库名>.git' -ForegroundColor Yellow
  Write-Host '      git push -u origin main' -ForegroundColor Yellow
  exit 1
}
Write-Host ('gh = ' + $gh) -ForegroundColor DarkGray

# ---- 1) 确认 git 仓库 ----
if (-not (Test-Path (Join-Path $root '.git'))) {
  Write-Host '== 初始化 git 仓库 ==' -ForegroundColor Cyan
  git init -b main | Out-Null
  git config core.autocrlf false
  git add -A
  git commit -m 'Codex 图块库 - AutoCAD 2024 图块库插件' | Out-Null
}

# ---- 2) 无改动则提交 ----
$dirty = git status --porcelain
if ($dirty) {
  Write-Host '== 提交本地改动 ==' -ForegroundColor Cyan
  git add -A
  git commit -m ('更新：' + (Get-Date -Format 'yyyy-MM-dd HH:mm')) | Out-Null
}

# ---- 3) 登录 ----
& $gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
  Write-Host '== 需要登录 GitHub（会打开浏览器，复制一次性验证码后确认）==' -ForegroundColor Cyan
  & $gh auth login --hostname github.com --git-protocol https --web
  if ($LASTEXITCODE -ne 0) { throw '登录失败，请重试 gh auth login' }
}

# ---- 4) 建仓库并推送 ----
$visibility = if ($Private) { '--private' } else { '--public' }
& $gh repo view $Repo *> $null
if ($LASTEXITCODE -eq 0) {
  Write-Host ('== 远程仓库已存在，直接推送 ==') -ForegroundColor Cyan
  $me = (& $gh api user --jq .login).Trim()
  $url = 'https://github.com/' + $me + '/' + $Repo + '.git'
  git remote remove origin 2>$null
  git remote add origin $url
  git push -u origin main
  if ($LASTEXITCODE -ne 0) { throw '推送失败' }
} else {
  Write-Host ('== 创建仓库并推送 ==') -ForegroundColor Cyan
  & $gh repo create $Repo --source . --push $visibility
  if ($LASTEXITCODE -ne 0) { throw '创建/推送失败' }
}

# ---- 5) 可选打 tag ----
if ($Tag) {
  Write-Host ('== 打 tag ' + $Tag + ' 并推送（会触发自动发版）==') -ForegroundColor Cyan
  git tag $Tag
  git push origin $Tag
  if ($LASTEXITCODE -ne 0) { throw 'tag 推送失败' }
  Write-Host ('已触发 CI，去 Actions 页面看进度：https://github.com/' + $Repo + '/actions') -ForegroundColor Green
}

Write-Host ''
Write-Host '完成。' -ForegroundColor Green
if (-not $Tag) {
  Write-Host '想发版就执行： .\push-to-github.ps1 -Tag v1.0.6' -ForegroundColor DarkGray
}