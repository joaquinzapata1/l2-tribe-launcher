@echo off
setlocal
cd /d "%~dp0"
title L2 Tribe Launcher - Live Preview
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0live-preview.ps1"
if errorlevel 1 (
  echo.
  echo El live preview fallo. El error queda arriba para poder revisarlo.
  pause
)
