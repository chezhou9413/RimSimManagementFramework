@echo off
pushd "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File ".\compile_modSelf.ps1" %*
set result=%errorlevel%
popd
exit /b %result%
