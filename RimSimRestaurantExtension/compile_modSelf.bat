@echo off
chcp 65001 >nul
setlocal EnableExtensions DisableDelayedExpansion

REM 转调仓库统一入口，按依赖顺序编译框架与餐厅扩展。
call "%~dp0..\compile_modSelf.bat" %*
exit /b %ERRORLEVEL%
