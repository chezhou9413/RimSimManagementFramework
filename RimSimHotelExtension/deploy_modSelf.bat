@echo off
pushd "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File ".\deploy_modSelf.ps1" %*
set result=%errorlevel%
popd
exit /b %result%
