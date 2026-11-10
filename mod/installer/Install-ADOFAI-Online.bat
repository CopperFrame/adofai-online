@echo off
rem ADOFAI Online installer: double-click this file. It runs Install-ADOFAI-Online.ps1 (same folder).
rem Add --uninstall to remove the mod.
if /I "%~1"=="--uninstall" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-ADOFAI-Online.ps1" -Uninstall
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-ADOFAI-Online.ps1"
)
