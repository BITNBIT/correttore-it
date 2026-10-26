@echo off
chcp 65001 >nul
title Correttore IT - Preparazione
if not exist "%~dp0prepara.ps1" (
  echo.
  echo  Estrai prima TUTTO lo zip in una cartella, poi avvia Prepara.bat da li'.
  echo.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0prepara.ps1" %*
