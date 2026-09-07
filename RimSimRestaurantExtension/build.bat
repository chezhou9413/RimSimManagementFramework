@echo off
chcp 65001 >nul
setlocal EnableExtensions DisableDelayedExpansion

REM 转调仓库统一编译入口，保留餐厅目录的构建命令。
call "%~dp0..\compile_modSelf.bat" %*
exit /b %ERRORLEVEL%
