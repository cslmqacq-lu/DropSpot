@echo off
chcp 65001 >nul

rem ---- 没有管理员权限时自动请求提权（USN 监视和 smoke test 需要） ----
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

cd /d "%~dp0.."
if not exist artifacts mkdir artifacts
set LOG=artifacts\verify-build.log
set EXE=bin\Debug\net8.0-windows\DropSpot.exe

rem ---- 先关闭正在运行的 DropSpot，否则无法覆盖 exe ----
tasklist /FI "IMAGENAME eq DropSpot.exe" 2>nul | find /I "DropSpot.exe" >nul
if not errorlevel 1 (
  echo 正在关闭运行中的 DropSpot...
  taskkill /IM DropSpot.exe >nul 2>&1
  timeout /t 3 /nobreak >nul
  taskkill /F /IM DropSpot.exe >nul 2>&1
  timeout /t 1 /nobreak >nul
)

rem ---- 保存上一次运行的程序日志，方便排查（拖放、权限等） ----
if exist "%APPDATA%\DropSpot\logs\DropSpot.log" copy /Y "%APPDATA%\DropSpot\logs\DropSpot.log" artifacts\app-last-run.log >nul

echo [build] %date% %time% > "%LOG%"
echo 正在编译...
dotnet build -c Debug -nologo -v minimal >> "%LOG%" 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> "%LOG%"
  echo 编译失败，详情见 %LOG%
  pause
  exit /b 1
)

echo 正在运行 smoke test...
echo [smoke] >> "%LOG%"
dotnet run -c Debug --no-build -- --smoke-test >> "%LOG%" 2>&1
set SMOKE=%errorlevel%
echo SMOKE EXIT CODE: %SMOKE% >> "%LOG%"
if not "%SMOKE%"=="0" (
  echo smoke test 未通过（退出码 %SMOKE%），详情见 %LOG%
  pause
  exit /b 1
)

echo 全部通过，正在以普通权限启动 DropSpot（与双击打开一致）...
rem 通过 explorer 启动，避免继承本脚本的管理员权限
explorer.exe "%CD%\%EXE%"
timeout /t 2 /nobreak >nul
