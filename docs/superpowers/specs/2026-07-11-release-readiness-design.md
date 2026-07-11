# DropSpot 正式发布完善设计

## 范围

本轮完成正式发布前的四项工作：使用用户提供的紫色双文件夹图片制作应用图标；限制正常运行实例为一个；在设置中增加随 Windows 启动开关；生成并验证可从旧 DiskWriteWatcher 升级的 DropSpot 安装版与便携版。

## 应用图标

- 原图保存为 `assets/DropSpot-icon-source.png`。
- 将横向原图置于匹配的紫色方形背景中，输出包含 16、24、32、48、64、128、256 像素图层的 `assets/DropSpot.ico`。
- EXE、窗口、任务栏和 Inno Setup 安装程序统一使用该图标。

## 单实例

- 正常启动使用当前 Windows 会话内的命名互斥体。
- 首个实例监听命名激活事件；第二个实例不创建监视器、不打开第二份设置文件，只发送激活事件后退出。
- 已有实例处于浮窗模式时恢复主窗口；已显示时激活并置前。
- `--smoke-test` 与 `--ui-smoke-test` 独立运行，不受正常实例互斥限制。

## 随 Windows 启动

- 设置窗口新增“常规”页和“随 Windows 启动”复选框，默认关闭。
- 使用当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限。
- 注册命令包含 `--startup`；开机启动时自动进入浮窗，不打扰桌面。
- 程序移动后，只要再次启动，就按设置用当前 EXE 路径修正注册表命令。
- 卸载 DropSpot 时删除对应启动项。

## 升级安装

- 沿用原安装器 `AppId`，识别旧版本安装记录。
- 新安装目录固定为 `%LocalAppData%\Programs\DropSpot`，不沿用旧目录。
- 安装前关闭旧 `DiskWriteWatcher.exe` 和新 `DropSpot.exe`；安装后删除旧默认安装目录中的旧程序、说明和卸载残留。
- `%AppData%\DiskWriteWatcher` 继续由应用首次启动复制到 `%AppData%\DropSpot`，安装器不删除用户设置。

## 验证

- 构建和完整 smoke test 通过，EXE 文件属性与图标为 DropSpot。
- 启动两个正常进程时，第二个快速退出，首个实例恢复并响应。
- 实际启用和关闭开机启动，核对 Run 注册表值及清理结果。
- 便携版解压后 smoke test 通过。
- 在旧版已安装状态上运行新安装包，确认旧进程关闭、旧 EXE 清理、DropSpot 安装运行、设置迁移和卸载清理均通过。
- 输出安装版、便携版 ZIP 和 SHA-256 校验文件。
