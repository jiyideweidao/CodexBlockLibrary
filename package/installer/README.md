# Codex 图块库（CodexBlockLibrary）v1.0.5

AutoCAD 插件：扫描其它图纸里的图块，统计**动态块**数量，按**分类 / 标签**管理，
并把任意图块（含动态参数）**复制 / 插入到当前正在绘制的图纸**中。

支持的来源文件格式：`dwg`、`dwt`（样板）、`dws`（标准）、`dxf`。

---

## 一、系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 / 11 64 位 |
| AutoCAD | **AutoCAD 2024 或更高版本**（完整版；不支持 AutoCAD LT） |
| .NET | .NET Framework 4.8（AutoCAD 2020 以上自带，一般无需单独安装） |
| 权限 | 安装到当前用户**不需要管理员权限** |

---

## 二、安装（推荐用 Setup.exe）

**方式一：一键安装（推荐）**

1. 双击 **`Setup.exe`**（只下载了 Setup.exe 的话，直接运行它就够了）。
2. 在窗口里选好「安装位置」，点 **安装插件**：
   - 安装给当前用户：不需要管理员权限；
   - 安装给本机所有用户：会弹出 UAC 授权。
3. 看到 `[OK] 安装完成` 后，**启动 AutoCAD**（已经开着的话请重启）。

**方式二：脚本安装（便携包）**

1. 把 `CodexBlockLibrary-1.0.5.zip` **完整解压**到任意目录（不要在压缩包里直接双击）。
2. 双击 **`install.cmd`**（装给当前用户，不弹 UAC）；
   要让本机所有用户都能用，改双击 **`install-allusers.cmd`**（会弹 UAC）。
3. 装完同样重启 AutoCAD。

> **静默安装**（适合批量部署 / 无人值守）：
> `Setup.exe /silent` 装给当前用户，`Setup.exe /silent /allusers` 装给所有用户（需管理员），
> `Setup.exe /silent /uninstall` 静默卸载，加 `/purge` 连用户配置一起删。
> 静默模式不显示界面，结果写入 `%TEMP%\CodexBlockLibrary-setup.log`，退出码 0 表示成功。

装好后 AutoCAD 启动时会自动加载插件：菜单栏出现 **块库(K)**，功能区长出 **块库** 选项卡。

> 也可以纯手动：把 `bundle\CodexBlockLibrary.bundle` 整个文件夹复制到
> `%APPDATA%\Autodesk\ApplicationPlugins\`
> （所有用户则是 `%PROGRAMDATA%\Autodesk\ApplicationPlugins\`）。

## 三、怎么用

| 入口 | 说明 |
| --- | --- |
| 命令行 `BLKLIB` | 打开 / 显示「Codex 图块库」面板 |
| 菜单栏 **块库(K)** | 经典菜单栏下拉菜单（插件会把 MENUBAR 自动设为 1） |
| 功能区 **块库** 选项卡 | 7 个按钮：块库面板 / 扫描图纸 / 重新扫描选中 / 暂停扫描 / 扫描当前图纸 / 导出统计表 / 设置 |

典型流程：

1. `BLKLIB` 打开面板 → 点 **添加文件夹** 或 **添加图纸**，选中要收集图块的 dwg/dwt/dws/dxf；
2. 面板左侧是**分类树**，右侧列出图块缩略图，下方可修改该图块的**分类**与**标签**；
3. 选中一个图块后：
   - **插入到图纸**：在当前图纸新建块参照（按单位自动换算比例）；
   - **粘贴原样**：克隆来源实例，**动态块的可见性状态与全部参数值完整保留**；
4. 只需要块定义时点 **仅导入定义**；点 **复制块名** 可把块名复制到剪贴板。

面板为**非模态**：插入过程中可以直接在图中取点，不用关面板。

### 面板折叠与关闭

- 面板**默认折叠成窄条**（只留工具条 + 状态栏），状态栏右侧有 **展开 / 折叠**、
  **卸载**、**关闭** 三个按钮；
- 命令行 `BLKCOLLAPSE` 也能折叠 / 展开，状态会被记住；
- 不想默认折叠：`BLKSETTINGS` → 取消「打开面板时默认折叠为窄条」。

### 常用命令

| 命令 | 功能 |
| --- | --- |
| `BLKLIB` | 打开图块库面板 |
| `BLKCLOSE` | 关闭面板（Esc 等效） |
| `BLKCOLLAPSE` | 折叠 / 展开面板 |
| `BLKSCAN` | 扫描文件或整个文件夹，结果加入块库 |
| `BLKRESCAN` | 只重新扫描列表中选中的图纸（插件不做全局扫描） |
| `BLKPAUSE` | 暂停 / 继续正在进行的扫描 |
| `BLKCOUNT` | 扫描**当前打开的这张图纸**并加入块库（动态块 / 静态块 / 外部参照统计，可导出 CSV） |
| `BLKSTATS` | 导出块库统计表 CSV |
| `BLKIMPORT` | 把来源图纸中的块定义导入当前图纸 |
| `BLKSETTINGS` | 设置：分类方式、缩略图、界面挂载、自定义分类规则 |
| `BLKSELFTEST` | 自检（环境 / 界面 / 扫描 / 复制插入 / 后台扫描） |
| `BLKUNINSTALL` | 卸载插件 |
| `BLKABOUT` | 关于与命令速查 |

---

## 四、卸载

任选一种：

- 双击 **`Setup.exe`**，点「卸载插件」；
- 面板状态栏点 **卸载**，或命令行 `BLKUNINSTALL`；
- 双击安装包里的 **`uninstall.cmd`**；
- 手动删除 `%APPDATA%\Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle`。

卸载前请**先关闭 AutoCAD**（插件 dll 被占用时无法删除）。
选择性删除用户配置：`%APPDATA%\Autodesk\CodexBlockLib\`
（内含已登记源图纸、自定义分类与标签、缩略图缓存、日志）。

---

## 五、常见问题

**Q：装完 AutoCAD 里没有「块库」菜单？**
A：bundle 只在 AutoCAD **启动时**自动加载。请完全退出 AutoCAD 再重新打开。
若仍然没有，在命令行执行 `NETLOAD`，选择
`...\CodexBlockLibrary.bundle\Contents\Windows\CodexBlockLib.UI.dll`，
然后看日志 `%APPDATA%\Autodesk\CodexBlockLib\codex-blocklib.log`。
另外：**经典菜单栏的「块库(K)」偶尔不会自动出现**（AutoCAD 启动瞬间 COM 调用可能被拒绝）。
执行一次 `BLKLIB` 即可立即补挂，插件在后台也会低频重试。

**Q：双击 `install.cmd` 一闪而过 / 提示脚本被禁止？**
A：安装脚本用 `-ExecutionPolicy Bypass` 启动 PowerShell，一般不会被策略拦住。
若公司组策略禁止脚本，可改用手动复制 bundle 的方式（见第二节末尾）。

**Q：面板打开是窄条，看不到内容？**
A：这是默认的折叠状态，点状态栏右侧的 **展开** 即可；状态会被记住。

**Q：面板打开了，但状态栏显示「已载入 0 张图纸 / 0 个块定义」？**

A：说明登记的源图纸路径都失效了（图纸被移动、改名或删除）。点面板工具栏的
**`修复失效路径`**：在弹出的对话框里对每条失效路径选 **`重新定位...`** 指向新位置，
或者用 **`移除选中` / `全部移除`** 把失效条目清掉，然后点 `重新扫描选中`。
这个对话框只调整插件的登记列表，**不会删除磁盘上的任何文件**。
日志 `%APPDATA%\Autodesk\CodexBlockLib\codex-blocklib.log` 里也会写明是哪些路径失效了
（`WARN 源图纸路径失效（文件已被移动或删除）: ...`）。

**Q：杀毒软件报警？**
A：本插件是本地 .NET 程序，不联网、不写注册表、不上传任何数据；
安装动作只是把文件复制到 `%APPDATA%` 下的插件目录。误报可放行。

**Q：支持 AutoCAD 2020 / 2021 / LT 吗？**
A：`PackageContents.xml` 声明的最低版本是 **R24.0（AutoCAD 2024）**，
低于该版本的 AutoCAD 不会加载；AutoCAD LT 不支持 .NET 插件。

---

## 六、目录结构

```
CodexBlockLibrary-1.0.5\
├─ Setup.exe              一键安装 / 卸载（推荐，双击运行）
├─ install.cmd            双击安装（当前用户）
├─ install-allusers.cmd   双击安装（所有用户，需 UAC）
├─ uninstall.cmd          双击卸载
├─ README.md              本文件
├─ tools\
│   ├─ install.ps1        安装脚本主体
│   └─ uninstall.ps1      卸载脚本主体
├─ bundle\
│   └─ CodexBlockLibrary.bundle\     要安装的插件包本体
└─ docs\
    ├─ 使用说明与实现说明.md
    └─ GitHub同类项目调研.md
```

## 七、说明

- 插件所有数据都保存在本机：`%APPDATA%\Autodesk\CodexBlockLib\`，
  统计表 CSV 导出到 `%USERPROFILE%\Documents\CodexBlockLib\`。
- 不带任何联网功能、不收集任何信息。
