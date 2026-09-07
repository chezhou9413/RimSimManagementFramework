@echo off
chcp 65001 >nul
setlocal EnableExtensions DisableDelayedExpansion

REM 转调仓库统一入口，一起编译并部署两个模组。
call "%~dp0..\deploy_modSelf.bat" %*
exit /b %ERRORLEVEL%
