@echo off
setlocal
cd /d "%~dp0"
title L2 Tribe Launcher - Preview
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0dev-preview.ps1"
if errorlevel 1 (
  echo.
  echo El preview fallo. El error queda arriba para poder revisarlo.
  pause
)
