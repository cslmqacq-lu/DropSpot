@echo off
chcp 65001 >nul
cd /d "%~dp0.."
if not exist artifacts mkdir artifacts
set LOG=artifacts\verify-build.log
echo [build] %date% %time% > "%LOG%"
dotnet build -c Debug -nologo -v minimal >> "%LOG%" 2>&1
if errorlevel 1 (
  echo BUILD FAILED >> "%LOG%"
  echo 编译失败，详情见 %LOG%
  pause
  exit /b 1
)
echo [smoke] >> "%LOG%"
dotnet run -c Debug --no-build -- --smoke-test >> "%LOG%" 2>&1
echo SMOKE EXIT CODE: %errorlevel% >> "%LOG%"
echo 完成，结果已写入 %LOG%（SMOKE EXIT CODE 为 0 表示全部通过）
pause
