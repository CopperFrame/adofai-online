@echo off
rem ADOFAI Online diagnostics: double-click this file AFTER starting the game once (open the custom level selector, then close the game).
rem It checks the installation and writes ADOFAI-Online-diagnostic.txt on your Desktop. Send that file to whoever is helping you.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-ADOFAI-Online.ps1" -Diagnose
