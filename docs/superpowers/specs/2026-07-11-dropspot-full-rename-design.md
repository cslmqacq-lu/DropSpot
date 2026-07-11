# DropSpot 完整改名设计

## 目标

将软件从“活跃文件夹 / DiskWriteWatcher”完整改名为 `DropSpot`，覆盖界面品牌、项目与程序集名称、命名空间、输出程序、未来分发配置、用户数据目录和工作区目录。本次不重新生成安装包或便携版 ZIP。

## 技术命名

- 项目文件改为 `DropSpot.csproj`。
- 默认命名空间改为 `DropSpot`。
- Release 输出程序改为 `DropSpot.exe`。
- 主窗口、浮窗、关于页、产品元数据和 README 使用 `DropSpot`。
- 未来打包脚本输出 `DropSpot_v<版本>_win-x64_portable.zip` 和 `DropSpot_Setup_v<版本>_win-x64.exe`。
- Inno Setup 保留原有 `AppId`，确保未来 DropSpot 安装包可以识别并升级旧安装记录。

## 设置迁移

- 新数据目录为 `%AppData%\DropSpot`。
- 首次启动时，如果新目录没有设置文件而 `%AppData%\DiskWriteWatcher` 存在，则复制旧 `settings.json` 与 `.bak` 到新目录。
- 迁移使用复制而非移动，旧版安装程序和旧数据保留，迁移失败时继续使用 DropSpot 默认设置并显示警告。
- 新目录一旦已有设置，不再读取或覆盖旧目录。

## 工作区与分发边界

- Git 项目目录最终改为 `G:\AI开发相关\DropSpot`。
- 现有未跟踪 `deliverables/` 内容保持不变，不纳入改名提交。
- 现有 `dist` 中的旧品牌安装包与 ZIP 不更新；未来再次运行打包脚本时才生成 DropSpot 分发文件。

## 验证

- Release 构建输出 `DropSpot.exe`，不再输出 `DiskWriteWatcher.exe`。
- Smoke test 与 UI smoke test 均通过。
- 临时旧设置可自动复制到新路径，且新路径存在时不会被旧设置覆盖。
- 当前真实 `%AppData%\DiskWriteWatcher` 设置迁移到 `%AppData%\DropSpot`，收藏与监视范围数量一致。
- 启动后窗口标题与文件元数据均显示 `DropSpot`。
