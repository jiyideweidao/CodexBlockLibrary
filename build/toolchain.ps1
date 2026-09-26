# CodexBlockLibrary 工具链探测：Roslyn csc / .NET Framework 4.8 参考程序集 / AutoCAD 托管程序集 / 7-Zip
# 由 build\build.ps1 与 package\make-package.ps1 共用，本地 _buildtools 与 GitHub Actions 均可解析。
# 所有路径都可用环境变量覆盖：CODEX_CSC / CODEX_REFS / ACAD_DIR / CODEX_7ZIP
function Resolve-CodexToolchain {
  param(
    [string]$ToolsRoot,
    [string]$Csc,
    [string]$Refs,
    [string]$AcadDir,
    [string]$SevenZip
  )
  $notes = New-Object System.Collections.Generic.List[string]

  function First-Hit([string[]]$Paths, [string]$Probe) {
    foreach ($p in $Paths) {
      if (-not $p) { continue }
      if ($Probe) { if (Test-Path (Join-Path $p $Probe)) { return $p } }
      elseif (Test-Path $p) { return $p }
    }
    return $null
  }

  # ---------- Roslyn csc ----------
  if (-not $Csc) {
    $cand = New-Object System.Collections.Generic.List[string]
    if ($ToolsRoot) { $cand.Add((Join-Path $ToolsRoot 'roslyn\tools\csc.exe')) }
    if ($env:CODEX_CSC) { $cand.Add($env:CODEX_CSC) }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
      $hit = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' 2>$null
      foreach ($f in @($hit)) { if ($f) { $cand.Add(($f -replace '\s+$','')) } }
    }
    if ($env:MSBUILD_EXE_PATH) { $cand.Add((Join-Path (Split-Path -Parent $env:MSBUILD_EXE_PATH) 'Roslyn\csc.exe')) }
    $Csc = First-Hit $cand $null
  }

  # ---------- .NET Framework 4.8 参考程序集 ----------
  if (-not $Refs) {
    $cand = New-Object System.Collections.Generic.List[string]
    if ($ToolsRoot) { $cand.Add((Join-Path $ToolsRoot 'net48refs\build\.NETFramework\v4.8')) }
    if ($env:CODEX_REFS) { $cand.Add($env:CODEX_REFS) }
    $cand.Add((Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'))
    $cand.Add((Join-Path $env:ProgramFiles 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'))
    if ($env:USERPROFILE) {
      Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net48') -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { $cand.Add((Join-Path $_.FullName 'build\.NETFramework\v4.8')) }
    }
    $Refs = First-Hit $cand 'mscorlib.dll'
  }

  # ---------- AutoCAD 托管程序集 ----------
  if (-not $AcadDir) {
    $cand = New-Object System.Collections.Generic.List[string]
    if ($env:ACAD_DIR) { $cand.Add($env:ACAD_DIR) }
    if ($ToolsRoot) { $cand.Add((Join-Path $ToolsRoot 'acad2024')) }
    $cand.Add('D:\Program Files\AutoCAD 2024')
    $cand.Add('C:\Program Files\Autodesk\AutoCAD 2024')
    Get-ChildItem 'C:\Program Files\Autodesk' -Directory -Filter 'AutoCAD 2024*' -ErrorAction SilentlyContinue |
      ForEach-Object { $cand.Add($_.FullName) }
    $AcadDir = First-Hit $cand 'acdbmgd.dll'
  }

  # ---------- 7-Zip ----------
  if (-not $SevenZip) {
    $cand = New-Object System.Collections.Generic.List[string]
    if ($env:CODEX_7ZIP) { $cand.Add($env:CODEX_7ZIP) }
    $cand.Add('C:\Program Files\7-Zip\7z.exe')
    $cand.Add('C:\Program Files (x86)\7-Zip\7z.exe')
    $found = Get-Command '7z' -ErrorAction SilentlyContinue
    if ($found) { $cand.Add($found.Source) }
    $SevenZip = First-Hit $cand $null
  }

  if (-not $Csc)     { $notes.Add('未找到 Roslyn csc.exe（可设 CODEX_CSC 或安装 VS/生成工具）') }
  if (-not $Refs)    { $notes.Add('未找到 .NET Framework 4.8 参考程序集（可设 CODEX_REFS）') }
  if (-not $AcadDir) { $notes.Add('未找到 AutoCAD 2024 托管程序集（可设 ACAD_DIR）') }
  if (-not $SevenZip) { $notes.Add('未找到 7-Zip，将回退到 .NET 内置压缩（可设 CODEX_7ZIP）') }

  return [pscustomobject]@{
    Csc      = $Csc
    Refs     = $Refs
    AcadDir  = $AcadDir
    SevenZip = $SevenZip
    Notes    = $notes
  }
}