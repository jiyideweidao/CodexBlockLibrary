# Codex 图块库（CodexBlockLibrary）— AutoCAD 2024 插件

扫描其它 `dwg / dwt / dws / dxf` 图纸中的图块，统计**动态块**数量，按**分类 / 标签**管理，
并把任意图块**复制 / 插入到当前正在绘制的图纸**。

- 版本：1.0.5
- 目标平台：AutoCAD 2024（R24.3，简体中文）+ .NET Framework 4.8
- 已安装位置（用户级，无需管理员）：
  `%APPDATA%\Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle`

## 一、安装 / 卸载

```powershell
# 编译 + 安装（用户级）
& "C:\Users\Admin（无密码）\Documents\ChatGPT\New project\CodexBlockLibrary\install\install.ps1"

# 只安装不编译
& "...\install\install.ps1" -SkipBuild

# 安装给所有用户（需要管理员权限）
& "...\install\install.ps1" -AllUsers
```

卸载有两种方式：

1. **面板内一键卸载（推荐）**：面板状态栏右侧的 **卸载** 按钮，或命令行 `BLKUNINSTALL`。
   流程：确认卸载 -> 询问是否同时删除用户配置 -> 立即断开自动加载（删除 `PackageContents.xml`）->
   尽量删除插件文件 -> 剩下被 AutoCAD 占用的 dll 由后台脚本在 **AutoCAD 退出后** 自动清除。
   结束后按提示 **重启 AutoCAD** 即完成卸载。
2. **手动卸载**：关闭 AutoCAD，删除
   `%APPDATA%\Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle` 即可。

> 一键卸载只对“装在 `ApplicationPlugins` 下的 `*.bundle`”生效；卸载前会校验目录名与
> `PackageContents.xml`，校验不通过会拒绝删除，避免误删其它文件。
> 用 `NETLOAD` 从源码目录加载的开发副本不走一键卸载（请在源码目录里删）。

安装后**重启 AutoCAD 2024**，插件按 `PackageContents.xml` 自动加载（`APPAUTOLOAD`）。
不想重启时，也可在命令行执行 `NETLOAD` 并选择
`...\CodexBlockLibrary.bundle\Contents\Windows\CodexBlockLib.UI.dll`。

## 二、菜单入口

| 入口 | 说明 |
| --- | --- |
| 功能区选项卡 **块库** | 5 个按钮：块库面板 / 扫描图纸 / 动态块统计 / 导出统计表 / 设置 |
| 功能区 **附加模块** 选项卡 | “Codex 图块库”面板（块库面板、动态块统计） |
| 经典菜单栏 **块库(K)** | 8 项下拉菜单；插件会自动把 `MENUBAR` 设为 1（可在设置中关闭） |

菜单栏下拉菜单挂在 AutoCAD 的 `CUSTOM` 菜单组下。
> 如果启动后**没看到**「块库(K)」菜单（AutoCAD 刚启动时 COM 偶尔会被拒绝），
> 执行一次 `BLKLIB` 或 `BLKSCAN` 都会自动补挂；插件在后台也会低频重试。

> AutoCAD 2024 已移除 COM 的 `MenuGroups.Add`，无法再新建菜单组，因此插件复用 `CUSTOM` 组。

## 二之二、面板折叠与关闭

面板默认以 **折叠为窄条** 的方式打开（只保留工具条与状态栏，不占绘图区）：

- 状态栏右侧 **展开 / 折叠** 按钮，或命令行 `BLKCOLLAPSE`，在两种状态间切换；
- 折叠状态会被记住（`library.xml` 的 `PanelCollapsed`），下次打开沿用；
- 若不想默认折叠：`BLKSETTINGS` -> 取消“打开面板时默认折叠为窄条”。

关闭面板（只是隐藏，随时用 `BLKLIB` 重新打开）：

- 面板底部状态栏右侧的 **关闭** 按钮（始终可见）
- 面板工具栏最右侧的 **关闭** 按钮（面板较窄时会收进 `>>` 溢出菜单）
- 快捷键 **Esc**（面板获得焦点时）
- 命令行 **`BLKCLOSE`**
- 面板标题栏自带的 ✕

关闭只是隐藏面板，随时用 `BLKLIB` 重新打开。

## 二之三、源图纸路径失效了怎么办

图块库登记的是**源图纸的绝对路径**。图纸被移动、改名或删除（例如清理临时目录）后，这些登记就会失效：
插件不会报错崩溃，但对应图纸里的块一个也扫不出来。

**怎么发现**

- 面板状态栏显示的是**实际扫到的**数量，例如「已载入 0 张图纸 / 0 个块定义」；
- 日志 `%APPDATA%\Autodesk\CodexBlockLib\codex-blocklib.log` 里，每条失效路径都会记一行
  `WARN 源图纸路径失效（文件已被移动或删除）: <路径>`；
- 点 `重新扫描选中` 时如果一条都扫不到，状态栏会直接说明有几条路径失效、该点哪个按钮。

**怎么修**

- 工具栏 **`修复失效路径`** 按钮：列出全部失效路径，三种处理方式——
  - `重新定位...`：给这条登记重新指定一个文件（原登记项被替换成新路径）；
  - `移除选中` / `全部移除`：把失效登记从列表里删掉；
  - 这个对话框只会**调整插件的登记列表，不会删除磁盘上的任何文件**。
- 处理完点 `重新扫描选中`（或重启 AutoCAD）即可恢复。
- 图纸只是换了位置的话，也可以先 `移除`，再用 `添加图纸` / `添加文件夹` 重新登记。

## 三、命令

| 命令 | 功能 |
| --- | --- |
| `BLKLIB` | 打开“Codex 图块库”面板（非模态，取点时可保持打开） |
| `BLKCLOSE` | 关闭图块库面板（面板工具栏“关闭”按钮、Esc 键等效） |
| `BLKCOLLAPSE` | 折叠 / 展开面板（折叠后只留工具条与状态栏） |
| `BLKUNINSTALL` | 卸载插件（删除 `ApplicationPlugins` 下的插件包，重启 AutoCAD 后生效） |
| `BLKSCAN` | 选择文件(F) 或文件夹(D) 扫描，结果加入块库 |
| `BLKRESCAN` | 只重新扫描**列表中选中的图纸**（等同面板工具栏「重新扫描选中」；插件不做任何全局扫描） |
| `BLKPAUSE` | 暂停 / 继续正在进行的扫描（面板工具栏「暂停扫描」；功能区与经典菜单也有入口） |
| `BLKCOUNT` | 扫描**当前打开的这张图纸**并加入块库（动态块/静态块/外部参照统计，可选导出 CSV） |
| `BLKSTATS` | 导出块库统计表 CSV（`%USERPROFILE%\Documents\CodexBlockLib\`） |
| `BLKIMPORT` | 把来源图纸中的块定义导入当前图纸（A=全部 / D=仅动态块） |
| `BLKSETTINGS` | 设置：分类方式、缩略图、界面挂载、自定义分类规则 |
| `BLKSELFTEST` | 自检（环境 / 界面 / 扫描 / 复制插入 / 后台扫描），报告见下 |
| `BLKABOUT` | 关于与命令速查 |

## 四、把图块复制到当前图纸（两种方式）

1. **插入到图纸**（默认）：在目标图纸中新建一个块参照，按单位自动换算比例，
   命令行提示“原样(Y)”时可选保留源实例的缩放与旋转。
2. **粘贴原样（保留动态参数）**：克隆来源实例本身（`WblockCloneObjects`），
   **动态块的可见性状态与全部参数值完全保留**，这是本插件相对“普通复制粘贴”的关键能力。

两种方式都使用深克隆，嵌套块、图层、文字样式、动态参数定义一并带入。
面板为**非模态**，插入过程中可直接在图中取点。

## 五、分类与标签

- 分类方式：按名称前缀（默认）/ 按来源图纸 / 按所在文件夹 / 按图层 / 按类型（动态/静态/外部参照）/
  按可见性状态 / 按自定义规则 / 不分类。
- 自定义规则：设置对话框内可配置“块名包含（或正则）→ 归入分类”，从上到下匹配。
- 标签：自动生成“类型 + 来源图纸 + 文件夹 + 前 3 个图层 + 可见性状态”，可在面板中手工修改。
- 用户手工设置的分类与标签保存在 `library.xml`，优先级高于自动分类。
## 六、配置文件与目录

| 路径 | 内容 |
| --- | --- |
| `%APPDATA%\Autodesk\CodexBlockLib\library.xml` | 设置、源图纸登记、分类、标签 |
| `%APPDATA%\Autodesk\CodexBlockLib\codex-blocklib.log` | 运行日志（4 MB 轮转） |
| `%APPDATA%\Autodesk\CodexBlockLib\thumbs\` | 块缩略图缓存（按图纸 MD5 分目录） |
| `%APPDATA%\Autodesk\CodexBlockLib\selftest.txt` | `BLKSELFTEST` 自检报告 |
| `%USERPROFILE%\Documents\CodexBlockLib\*.csv` | `BLKSTATS` 导出的统计表（UTF-8 BOM） |

## 七、项目结构与编译

```
CodexBlockLibrary\
├─ src\Core\                核心库（不依赖界面）
│   ├─ DrawingReader.cs     打开 dwg/dwt/dws/dxf 的旁路数据库
│   ├─ BlockScanner.cs      块统计引擎（动态/静态/外部参照、实例数、变体、缩略图）
│   ├─ DynamicBlockDetector.cs  动态块识别（BlockTableRecord.IsDynamicBlock + RXClass 参数/动作类扫描）
│   ├─ GeometryCapture.cs   从块定义提取线框，用于生成缩略图
│   ├─ ThumbnailRenderer.cs GDI+ 缩略图渲染与磁盘缓存
│   ├─ Importer.cs          导入定义 / 插入参照 / 克隆原实例
│   ├─ LibraryStore.cs      设置、分类规则、XML 持久化
│   └─ ReportWriter.cs      CSV 与命令行摘要
├─ src\UI\                  界面与命令
│   ├─ Plugin.cs            IExtensionApplication，启动加载
│   ├─ RibbonMenu.cs        功能区选项卡 / 附加模块面板 / 经典菜单栏
│   ├─ PaletteHost.cs       PaletteSet 宿主
│   ├─ MainPalette.cs       块库面板（树 / 缩略图列表 / 详情 / 统计表）
│   ├─ SettingsForm.cs      设置对话框
│   ├─ MissingFilesForm.cs  失效源图纸路径的重新定位 / 移除
│   ├─ Commands.cs          对外的 11 个命令
│   ├─ SelfTest.cs          自检
│   └─ Uninstaller.cs       一键卸载（路径校验 + 退出后后台清理）
├─ build\build.ps1          编译（Roslyn csc + .NET Framework 4.8 参考程序集，无需 VS/MSBuild）
├─ build\toolchain.ps1      工具链探测（csc / 参考程序集 / AutoCAD 程序集 / 7-Zip，本地与 CI 通用）
├─ package\make-package.ps1 打包：Setup.exe + 便携 zip + SHA256
├─ package\setup\Setup.cs   单文件安装程序源码（GUI + 静默）
├─ package\installer\       便携包里的安装/卸载脚本与 README
├─ .github\workflows\release.yml  打 tag 自动打包并发 Release
├─ .gitignore               忽略 dist / release / _backup / _shots / refs
├─ refs\acad2024\           （可选）放 AutoCAD 托管程序集，供无 AutoCAD 的机器编译
├─ install\install.ps1      开发用：编译 + 安装到 ApplicationPlugins
├─ bundle\CodexBlockLibrary.bundle\   插件包（PackageContents.xml + Contents）
└─ dist\Contents\Windows\   编译输出
```

编译（无需 Visual Studio / MSBuild，用仓库内的 Roslyn 编译器）：

```powershell
& "...\CodexBlockLibrary\build\build.ps1"          # Core + UI
& "...\CodexBlockLibrary\build\build.ps1" -CoreOnly
```

## 八、自检（推荐安装后跑一次）

AutoCAD 命令行执行 `BLKSELFTEST`：

1. 自检范围：`C` 仅当前图纸 / `F` 附加扫描某个文件夹；
2. 自检阶段：直接回车=全部，或输入组合字母 `E` 环境 / `U` 界面 / `S` 扫描 / `V` 复制插入 / `B` 后台扫描；
3. 结果写入 `%APPDATA%\Autodesk\CodexBlockLib\selftest.txt`。

本机实测结果（AutoCAD 2024 简体中文，示例图纸）：

- 功能区选项卡：存在（Id=CODEX_BLOCKLIB_TAB，标题=块库，面板=1，按钮=5）
- 菜单栏：`块库(&K)` 位于菜单组 `CUSTOM`，菜单栏共 14 项
- 扫描 11 张图纸，识别到动态块 `Drawing Title`（动态块定义 1 / 实例 2）
- 导入块定义 成功 → 插入参照 成功（参照数 0→1）→ 原样粘贴 成功（1→2）
- 粘贴后动态参数读回：`Distance=2.583`、`Origin=(0.842,-0.513,0)`；目标块定义 `动态块=True`
- 单位换算：源“英寸” → 当前“毫米”，插入比例 25.4

## 九、已知限制

- `accoreconsole.exe`（无界面核心控制台）中无法创建功能区 / 菜单 / 面板，
  且在其进程内执行“导入 / 插入 / 克隆”会触发 CLR 栈溢出；自检已在该环境自动降级为
  “环境 + 扫描”两个阶段。请在完整版 AutoCAD 中使用本插件。
- 经典菜单栏在 AutoCAD 2024 中默认隐藏（`MENUBAR=0`），插件会自动置 1；如不希望改动，
  可在设置中取消“自动显示经典菜单栏”。
- 动态块的“变体”按参数值组合统计；同一块定义实例极多时，读取参数有上限
  （`MaxPropertyReadsPerBlock`，默认 60），避免大图纸卡顿。
- **AutoCAD 的 Database / Document API 只能在主线程（文档线程）使用**。插件已把全部图纸读写
  集中到主线程队列 `MainThreadPump`（由 `Application.Idle` + WinForms 计时器驱动）里逐个执行。
  新增功能时请不要在后台线程调用 `Database.ReadDwgFile`：AutoCAD 原生代码会在该调用链中访问
  WPF 调色板主题，跨线程异常穿透原生栈帧后托管 `try/catch` 无法拦截，会直接以
  “致命错误: Unhandled e0434352h Exception” 终止整个 AutoCAD 进程。

## 十、打包与分发（做安装包给别人用）

打包脚本：`package\make-package.ps1`，产物输出到 `release\` 目录。

```powershell
& "...\CodexBlockLibrary\package\make-package.ps1"              # 先编译再打包
& "...\CodexBlockLibrary\package\make-package.ps1" -SkipBuild  # 用现有 dist 打包
& "...\CodexBlockLibrary\package\make-package.ps1" -Version 1.1.0
```

### 产物说明

| 文件 | 大小 | 用途 |
| --- | --- | --- |
| `CodexBlockLibrary-1.0.5-Setup.exe` | ~106 KB | 单文件安装程序，内嵌整个插件包，双击即可用 |
| `CodexBlockLibrary-1.0.5.zip` | ~184 KB | 便携包：插件包 + 安装/卸载脚本 + 文档（含同一份 Setup.exe） |
| `SHA256SUMS.txt` | — | 上面两个文件的 SHA256 校验值 |

别人拿到的分发包里包含：

```
CodexBlockLibrary-1.0.5\
├─ Setup.exe               单文件安装程序（推荐）
├─ install.cmd             双击安装（当前用户，不需要管理员）
├─ install-allusers.cmd    双击安装（所有用户，会弹 UAC）
├─ uninstall.cmd           双击卸载
├─ tools\install.ps1       脚本版安装器（支持 -DryRun / -AllUsers）
├─ tools\uninstall.ps1     脚本版卸载器（支持 -KeepConfig / -DryRun）
├─ bundle\CodexBlockLibrary.bundle\   插件本体
├─ docs\                   使用说明 + GitHub 同类项目调研
└─ README.md               完整说明
```

### 安装方式一：Setup.exe（推荐给普通用户）

双击 `Setup.exe`，界面里可选：

- **安装位置**：`安装给当前用户`（不需要管理员）/ `安装给本机所有用户`（会弹 UAC 自提权）
- **卸载时一并删除用户配置**：勾选后卸载时连已登记图纸、分类标签、缩略图缓存、日志一起删
- 四个按钮：`安装插件` / `卸载插件` / `查看说明` / `关闭`，下方日志区会显示探测结果

安装程序会自动探测本机 AutoCAD 安装路径，并在 AutoCAD 正在运行时提示重启。

### 安装方式二：便携包脚本

解压 zip 后双击 `install.cmd` 即可（等价于 `Setup.exe` 的当前用户安装）。
命令行/静默用法：

```powershell
# 静默安装到当前用户
.\Setup.exe /silent
# 静默安装到所有用户（自动提权）
.\Setup.exe /allusers /auto
# 静默卸载（保留用户配置）
.\Setup.exe /silent /uninstall
# 静默卸载并清除用户配置
.\Setup.exe /silent /uninstall /purge

# 脚本版
powershell -ExecutionPolicy Bypass -File .\tools\install.ps1 -DryRun
powershell -ExecutionPolicy Bypass -File .\tools\uninstall.ps1 -KeepConfig
```

静默模式不显示界面，日志写入 `%TEMP%\CodexBlockLibrary-setup.log`，退出码 `0`=成功 / `1`=失败。

### 安装位置

- 当前用户：`%APPDATA%\Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle`
- 所有用户：`%PROGRAMDATA%\Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle`

安装后启动 AutoCAD，命令行输入 `BLKLIB` 打开面板，或在菜单栏点 `块库(K)`。

### 校验下载文件

```powershell
Get-FileHash .\CodexBlockLibrary-1.0.5-Setup.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

### 分发注意

- `Setup.exe` 未做代码签名，首次运行可能出现 SmartScreen“未知发布者”，
  选“更多信息 → 仍要运行”即可；也可以把 `Setup.exe` 与 `SHA256SUMS.txt` 一起提供。
- 用户级安装不需要管理员权限；机器级安装需要 UAC 授权。
- 卸载安全性：安装程序只允许删除 `ApplicationPlugins\` 下校验过
  （目录名含 `.bundle` 且带 `PackageContents.xml`）的插件目录，不会误删其他文件。

## 十一、代码签名（去掉“未知发布者”）

`Setup.exe` 没签名时，别人第一次运行会看到 SmartScreen 的“Windows 已保护你的电脑 → 未知发布者”。
两种解决办法：

### 办法一：买正规代码签名证书（推荐对外分发）

需要 OV 或 EV 代码签名证书（DigiCert / Sectigo / GlobalSign 等）。拿到 `.pfx` 后用
Windows SDK 自带的 `signtool.exe` 签名：

```powershell
# 找到 signtool
$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe |
            Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1

# 签名（带 RFC3161 时间戳，证书过期后签名依然有效）
& $signtool.FullName sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
    /f mycert.pfx /p <证书密码> `
    /d "Codex 图块库 AutoCAD 插件" `
    .\CodexBlockLibrary-1.0.5-Setup.exe

# 验证
Get-AuthenticodeSignature .\CodexBlockLibrary-1.0.5-Setup.exe | Format-List Status, SignerCertificate
```

`Status` 显示 `Valid` 即成功。注意：**签名会改变文件内容，签完必须重算 `SHA256SUMS.txt`**，
否则校验值对不上（仓库里的 CI 已经自动处理了这一步）。

### 办法二：自签名证书（只给自己 / 内部受控环境用）

```powershell
# 1) 生成代码签名证书（5 年有效）
$cert = New-SelfSignedCertificate -Type CodeSigningCert `
        -Subject "CN=Codex BlockLibrary" `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(5)

# 2) 导出 pfx（签名用）和 cer（给用户安装用）
$pwd = ConvertTo-SecureString -String '你的密码' -Force -AsPlainText
Export-PfxCertificate  -Cert $cert -FilePath mycert.pfx -Password $pwd
Export-Certificate     -Cert $cert -FilePath mycert.cer

# 3) 对 Setup.exe 签名
& $signtool.FullName sign /fd SHA256 /f mycert.pfx /p '你的密码' .\CodexBlockLibrary-1.0.5-Setup.exe
```

自签名证书默认**不被信任**，用户要先把它装进“受信任的根证书颁发机构”才不会再报警：

```powershell
# 用户端执行（需要管理员）
Import-Certificate -FilePath .\mycert.cer -CertStoreLocation Cert:\LocalMachine\Root
```

自签名只适合自己或小范围内部使用，对外公开发布请用办法一。

### 在 CI 里自动签名

仓库 `Settings → Secrets and variables → Actions` 里配置：

- Variable `SIGN_ENABLED` = `true`（开关）
- Secret `SIGN_PFX_BASE64`：`[Convert]::ToBase64String([IO.File]::ReadAllBytes('mycert.pfx'))` 的结果
- Secret `SIGN_PFX_PASSWORD`：pfx 密码

配好之后，`.github\workflows\release.yml` 里的“代码签名（可选）”步骤会自动签名并重算校验值。
## 十二、GitHub Actions 自动发布

工作流：`.github\workflows\release.yml`（Windows runner）。

**触发方式**

- 打 tag 推送即自动发布：`git tag v1.0.5 && git push origin v1.0.5`
- 或在 Actions 页面手动 `Run workflow`，可填版本号、可勾选“跳过编译”

**流程**

1. 检出代码，自动定位项目目录（仓库根 = `CodexBlockLibrary` 或它的上级都行）
2. 解析版本号（tag 名去掉前缀 `v`）
3. 准备 .NET Framework 4.8 参考程序集：优先用系统目标包，没有就下 NuGet 的
   `Microsoft.NETFramework.ReferenceAssemblies.net48`
4. 准备 Roslyn 编译器：优先用 VS 自带的 `csc.exe`，没有就下 NuGet 的
   `Microsoft.Net.Compilers.Toolset`
5. 探测 AutoCAD 2024 托管程序集 →
   **找不到就自动跳过编译，直接用仓库 `bundle\Contents\Windows` 里已有的 DLL 打包**
6. 打包出 `Setup.exe` + 便携 zip + `SHA256SUMS.txt`
7. （可选）代码签名
8. 上传构建产物；如果是 tag 触发，同时建好 GitHub Release 并附带三个文件

**关于编译**：插件的编译依赖 AutoCAD 2024 的 `acdbmgd.dll` / `accoremgd.dll` /
`acmgd.dll` / `AdWindows.dll`，这些 DLL 有版权、不能提交到仓库，所以 GitHub 托管 runner 上
默认是“跳过编译、直接用已提交的 DLL 打包”（产物与本地编译一致）。
想让 CI 真正重新编译，三选一：

- 仓库变量 `ACAD_DIR` 指向含这四个 DLL 的目录；
- 把 DLL 放到 `refs\acad2024\`（已在 `.gitignore` 里，不会被误提交）；
- 用装了 AutoCAD 2024 的**自建 runner**（`runs-on` 改成 `self-hosted`）。

**首次使用**：把 `CodexBlockLibrary` 目录初始化成 git 仓库推到 GitHub 即可：

```powershell
cd "...\CodexBlockLibrary"
git init
git add .
git commit -m "Codex 图块库 1.0.5"
git remote add origin https://github.com/<你的账号>/CodexBlockLibrary.git
git push -u origin main
git tag v1.0.5
git push origin v1.0.5        # 打完 tag 就会自动出一个 Release
```

**本地复现 CI 的打包（不编译）**

```powershell
& .\package\make-package.ps1 -SkipBuild -Version 1.0.5
```


## 十三、更新日志

**1.0.5**

- 「统计当前图纸」改名为「**扫描当前图纸**」：功能就是“我打开哪张图，就扫哪张图”——读取当前打开图纸的图块并加入块库，不碰其它文件。
- 插件不再有任何**全局扫描**：打开面板不扫描，也不会因为点某个按钮而一次扫描全部已登记图纸。
  只有你在插件里**明确指定的文件**才会被扫描——「添加图纸 / 添加文件夹」选择的范围，或列表里选中的图纸。
- 工具栏「重新扫描」改为「**重新扫描选中**」：只重扫列表里选中的那几张图纸；没有选中时只给提示，不做任何扫描。
  命令行 `BLKRESCAN` 同步改为“重扫选中”。
- 「修复失效路径」处理完后不再顺带触发全局重扫，只更新源图纸列表。

**1.0.4**

- 新增「暂停扫描」：面板工具栏的暂停 / 继续按钮、命令行 `BLKPAUSE`，功能区与经典菜单里也各有一个入口；
  扫描可以随时停下，需要时原地接着跑，不用从头再来。
- 暂停用「按原因」的队列机制：面板隐藏与用户手动暂停互不干扰——重新显示面板不会把用户暂停的扫描
  悄悄跑起来，关掉面板也不会丢掉用户暂停的状态。
- 扫描进行中再点一次「重新扫描」会直接继续暂停中的扫描，避免误以为卡死。

**1.0.3（重要修复）**

- 修复**扫描/打开图纸时 AutoCAD 弹出“致命错误: Unhandled e0434352h Exception”并崩溃**的问题。
  原因：扫描与缩略图原先在后台线程（`Task.Factory.StartNew`）里调用 `Database.ReadDwgFile`，
  而 AutoCAD 原生代码在该调用链中会查询 WPF 调色板主题（`PaletteTheme.IsDark` ->
  `Dispatcher.VerifyAccess`），跨线程异常穿过原生栈帧后无法被托管 `try/catch` 捕获，直接终止进程。
  现在所有图纸读写都排入主线程队列 `MainThreadPump` 执行，并在 `DrawingReader.Open` 加了
  主线程守卫：万一还有漏网的调用点，只会让该图纸扫描失败并写日志，绝不会再拖垮 AutoCAD。
- 扫描改为在主线程空闲时逐张图纸进行，界面不再长时间无响应，进度条与状态实时刷新。
- 「统计当前图纸」在读取当前文档数据库前先 `LockDocument()`，避免 `eLockViolation`。
- 不再自动扫描：面板打开时只提示已登记的源图纸数量，扫描由用户点「重新扫描」触发；
  新增命令行 `BLKRESCAN`，以及功能区/经典菜单里的「重新扫描」入口，便于脚本化与快速调用。
- 修复**关闭面板后 AutoCAD 仍然卡顿**的问题：关闭只是隐藏面板，原先排队的扫描与缩略图任务会继续
  在主线程逐张打开图纸；现在面板一旦不可见就暂停队列（含标题栏 × 与 Esc 关闭），重新打开后自动接着跑。
- 缩略图改为**按来源图纸批量生成**：同一张图纸只打开一次数据库，不再每个图块重开一次 DWG。

**1.0.2**

- 修复经典菜单栏「块库(K)」在启动瞬间 COM 被拒后不再重试、整个会话都挂不上菜单的问题；
  现在执行一次 `BLKLIB` 即可立即补挂。

**1.0.1**

- 新增「修复失效路径」：源图纸被移动或删除后，可重新定位或移除失效条目。
