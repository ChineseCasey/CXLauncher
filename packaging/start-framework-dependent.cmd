@echo off
setlocal
where dotnet >nul 2>nul
if errorlevel 1 goto missing
dotnet --list-runtimes | findstr /C:"Microsoft.WindowsDesktop.App 10.0." >nul
if errorlevel 1 goto missing
start "" "%~dp0CXLauncher.exe"
exit /b 0
:missing
echo CX Launcher requires the x64 .NET 10 Desktop Runtime.
echo Download: https://dotnet.microsoft.com/download/dotnet/10.0
echo You can also use the self-contained portable package.
pause
exit /b 1
