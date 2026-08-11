@echo off
setlocal
REM SigXor 一键发布：自包含独立 exe（无需安装 .NET）
REM 用法：
REM   scripts\publish.bat
REM   scripts\publish.bat win-arm64
REM   scripts\publish.bat win-x64 zip

cd /d "%~dp0.."

set RUNTIME=win-x64
set ZIP=

if /I "%~1"=="win-x64" set RUNTIME=win-x64
if /I "%~1"=="win-arm64" set RUNTIME=win-arm64
if /I "%~1"=="zip" set ZIP=-Zip
if /I "%~2"=="zip" set ZIP=-Zip

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" -Runtime %RUNTIME% %ZIP%
exit /b %ERRORLEVEL%
