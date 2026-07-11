# Windows 分发打包设计

## 目标

为 DiskWriteWatcher `1.0.0` 同时生成可直接分发的便携版 ZIP 和 Windows 安装包。目标电脑无需预装 .NET 运行时。

## 应用信息

- 产品名称：活跃文件夹
- 程序文件名：`DiskWriteWatcher.exe`
- 版本：从项目文件读取，本次为 `1.0.0`
- 开发者：`cslm`
- 运行平台：Windows x64

设置窗口的“关于”页继续动态显示程序版本与开发者；可执行文件元数据同步写入作者、公司、产品和版权信息。

## 便携版

- 使用 .NET 8 `win-x64` 自包含单文件发布。
- 启用单文件压缩和原生库自解压，不附带 PDB。
- ZIP 内包含 `DiskWriteWatcher.exe` 和中文 `README.txt`。
- 文件名为 `DiskWriteWatcher_v1.0.0_win-x64_portable.zip`。

## 安装版

- 使用 Inno Setup 6 生成 x64 安装程序。
- 默认安装到 Windows 标准应用目录。
- 创建开始菜单快捷方式，桌面快捷方式作为可选任务。
- 支持安装后启动与标准卸载入口。
- 不删除 `%AppData%\DiskWriteWatcher`，确保覆盖安装和卸载不会清空用户设置。
- 文件名为 `DiskWriteWatcher_Setup_v1.0.0_win-x64.exe`。

## 校验与复现

- 提供 PowerShell 打包脚本，一次完成构建、发布、ZIP、安装包和 SHA-256 校验文件生成。
- 打包前运行 Release 构建和 smoke test。
- 从便携版解压目录运行 smoke test。
- 静默安装到临时目录，从安装结果运行 UI smoke test，再静默卸载。
- `dist` 与中间产物不纳入 Git，只提交可复现的脚本、说明和安装配置。

## 边界

本次不包含代码签名证书、自动更新服务、ARM64/x86 构建或 Microsoft Store 包。未签名安装包可能触发 Windows SmartScreen 提示。
