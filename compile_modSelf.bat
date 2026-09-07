@echo off
chcp 65001 >nul
setlocal EnableExtensions DisableDelayedExpansion

REM 编译框架和餐厅扩展，并向调用者返回构建结果。
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0compile_modSelf.ps1" %*
exit /b %ERRORLEVEL%
