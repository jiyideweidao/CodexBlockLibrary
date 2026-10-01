Codex 图块库 (CodexBlockLibrary) v1.0.5
=========================================
AutoCAD 2024 插件：扫描其它 dwg / dwt / dws / dxf 图纸中的图块，
统计动态块数量，按分类与标签管理，并把图块复制/插入到当前图纸。

安装位置
--------
  用户级 : %APPDATA%\Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle
  所有用户: %PROGRAMDATA%\Autodesk\ApplicationPlugins\CodexBlockLibrary.bundle

命令
----
  BLKLIB       打开“Codex 图块库”面板（也可点功能区“块库”选项卡）
  BLKSCAN      扫描图纸/文件夹并加入块库
  BLKRESCAN    只重新扫描列表中选中的图纸（插件不做全局扫描）
  BLKPAUSE     暂停 / 继续正在进行的扫描
  BLKCOUNT     扫描当前打开的图纸并加入块库（可选导出）
  BLKSTATS     导出块库统计表（CSV）
  BLKIMPORT    把来源图纸中的块定义导入当前图纸
  BLKSETTINGS  设置（分类方式、缩略图、界面挂载、自定义分类规则）
  BLKSELFTEST  环境自检，报告写入 %APPDATA%\Autodesk\CodexBlockLib\selftest.txt
  BLKABOUT     关于

配置文件
--------
  %APPDATA%\Autodesk\CodexBlockLib\library.xml    块库设置与分类/标签
  %APPDATA%\Autodesk\CodexBlockLib\codex-blocklib.log  运行日志
  %APPDATA%\Autodesk\CodexBlockLib\thumbs\         缩略图缓存

提示
----
  面板为“非模态”窗口，插入/粘贴过程中可直接在图纸中取点。
  粘贴原样 = 克隆来源实例，动态块的参数值与可见性状态完全保留。
  源图纸路径失效时（图纸被移动 / 删除），点面板工具栏“修复失效路径”重新定位或移除登记。