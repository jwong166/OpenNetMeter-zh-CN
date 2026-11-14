@echo off
rem Compile-only check: no packaging, finishes fast, surfaces compile errors.
rem Always pass the .csproj path, never let dotnet pick the .sln (it also builds
rem the legacy WPF project and the WiX installer, which are not part of the app).
setlocal
cd /d "%~dp0"
dotnet build ".\OpenNetMeter\OpenNetMeter.csproj" -c Release --nologo
if errorlevel 1 (
  echo.
  echo Build FAILED. Copy the first error lines shown above.
  pause
  exit /b 1
)
echo.
echo Build OK. Run build-zh-CN.cmd to produce the single-file exe.
pause
