# GitHub 同类项目调研（2026-09-26）

调研目的：在自研之前先确认 GitHub 上是否已有可直接复用的“CAD 图块库 / 动态块”方案。
调研方式：GitHub 搜索 API（`api.github.com/search/repositories`），关键词组合与结果如下。

## 搜索关键词与命中

| 关键词 | 命中总数 | 代表性项目 |
| --- | --- | --- |
| `autocad dynamic block` | 13 | `eugenio-cunha/dynamic-block`、`ceybers/autocad-dynamic-block-script-generator`、`jeyapandiv/autocad-dynamic-block-configurator` |
| `autocad block library` | 11 | `manufino/AutoCAD`(★64)、`alanleite/autolisp-bb`、`neiljackson1984/autocad_block_library_utility` |
| `autocad blocks palette` | 1 | `JouniKort/AcadPalettes`（C# .NET 插件，含块操作面板） |
| `autocad block counter` | 2 | `Mickey0207/AutoCAD_Block_Counter`（C#，仅计数） |
| `cad 图块 插件` | 2 | `xiehaoing/MoYu-CAD-Gallery`（AutoLISP+OpenDCL 图库）、`ceciliading23-art/ChemSymbolSearch-AutoCAD` |
| `autocad dynamic block palette` / `图块库 autocad` | 0 | 无 |

## 结论

1. **没有可直接复用的“Codex skill”**：Codex 官方/社区技能库里没有 AutoCAD 图块库类技能；
   本机已装的是 `autocad-2024-mcp`（用 MCP 驱动 AutoCAD 画图），与本需求不同。
2. **GitHub 上有一批同题材项目，但都只覆盖需求的一部分**：
   - `MoYu-CAD-Gallery`：图库 + 缩略图 + 分类浏览 + 快速插入（AutoLISP/OpenDCL），
     但只针对“当前已有图库”，不能跨图纸统计块、不识别动态块、无标签体系。
   - `AcadPalettes`：.NET 面板程序集（技术栈最接近本插件），但只做块属性值操作。
   - `AutoCAD_Block_Counter`：只做块计数，不含动态块分类/复制插入。
   - `dynamic-block*` 系列：LISP/VBA 脚本，用于“批量插入/导出 YAML”，不能浏览其它图纸。
3. **AutoCAD 2024 自带能力（作为对标基线）**：
   - `设计中心 ADCENTER`：可从其它图纸复制块/图层/样式。
   - `块选项板 BLOCKSPALETTE`：可插入其它图形中的块，但不统计、不分类、不显示动态块参数。
   - `.dws` 仅作为“CAD 标准”检查文件使用，不自带图块浏览。
4. 因此本项目自研，并在自带能力之上补齐：
   **多格式读取(.dwg/.dxf/.dwt/.dws) → 动态块识别与计数 → 分类+标签 → 缩略图浏览 → 单块复制/插入（保留动态参数）**。

## 需求覆盖对比

| 能力 | AutoCAD 自带 | GitHub 同类 | 本插件 |
| --- | --- | --- | --- |
| 读取其它 .dwg/.dxf/.dwt/.dws | 部分（设计中心） | 部分 | 支持 |
| 统计动态块个数/实例数 | 否 | 否 | 支持 |
| 分类/标签管理 | 否 | 部分（仅图库分类） | 支持 |
| 缩略图浏览 | 部分 | 部分 | 支持 |
| 单个块复制到当前图纸 | 支持 | 支持 | 支持 |
| 粘贴时保留动态参数/可见性 | 否 | 否 | 支持（原样粘贴） |
| 统计表导出 CSV | 否 | 否 | 支持 |