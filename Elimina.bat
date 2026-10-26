@echo off
chcp 65001 >nul
title Correttore IT - Elimina
echo.
echo  Questo chiude Correttore IT, toglie l'avvio automatico e cancella la cartella:
echo  %~dp0
choice /c SN /m " Continuare"
if errorlevel 2 exit /b 0

taskkill /f /im CorrettoreIT.exe >nul 2>&1
powershell -NoProfile -Command "Get-Process llama-server -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '%~dp0*' } | Stop-Process -Force" >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v CorrettoreIT /f >nul 2>&1

echo.
echo  Fatto. Ricorda di rimuovere anche l'estensione da Chrome: chrome://extensions
echo.
pause
cd /d "%TEMP%"
start "" /min cmd /c "ping 127.0.0.1 -n 3 >nul & rmdir /s /q "%~dp0""
