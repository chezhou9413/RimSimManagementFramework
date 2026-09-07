@echo off
chcp 65001 >nul
setlocal EnableExtensions DisableDelayedExpansion

REM 统一编译并部署框架和餐厅扩展。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy_modSelf.ps1" %*
exit /b %ERRORLEVEL%
