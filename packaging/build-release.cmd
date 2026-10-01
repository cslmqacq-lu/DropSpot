@echo off
chcp 65001 >nul

rem ---- 一键打包：Release 构建 + smoke test + 单文件便携版 + Inno Setup 安装包 ----
rem smoke test 与 verify-build 一样需要管理员权限，这里自动请求提权
net session >nul 2>&1
if errorlevel 1 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

cd /d "%~dp0.."
if not exist artifacts mkdir artifacts
set LOG=artifacts\build-release.log

tasklist /FI "IMAGENAME eq DropSpot.exe" 2>nul | find /I "DropSpot.exe" >nul
if not errorlevel 1 (
  echo 正在关闭运行中的 DropSpot...
  taskkill /IM DropSpot.exe >nul 2>&1
  timeout /t 3 /nobreak >nul
  taskkill /F /IM DropSpot.exe >nul 2>&1
  timeout /t 1 /nobreak >nul
)

echo [release] %date% %time% > "%LOG%"
echo 正在打包，大约需要 1-3 分钟...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" >> "%LOG%" 2>&1
set RESULT=%errorlevel%
echo RELEASE EXIT CODE: %RESULT% >> "%LOG%"
if not "%RESULT%"=="0" (
  echo 打包失败，详情见 %LOG%
  pause
  exit /b 1
)

echo 打包完成，安装包在 dist 文件夹：
findstr /B "INSTALLER= PORTABLE=" "%LOG%"
explorer.exe "%CD%\dist"
pause
