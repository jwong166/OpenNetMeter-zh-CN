@echo off
rem One-click build for the Simplified Chinese edition.
rem Usage:  build-zh-CN.cmd              (win-x64, Release)
rem         build-zh-CN.cmd win-arm64    (other runtime)
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-zh-cn.ps1" %*
if errorlevel 1 (
  echo.
  echo Build FAILED. See the messages above.
  pause
  exit /b 1
)
echo.
pause
