@echo off
rem PrestigePerks tester tool: change the prestige level of an SPT profile (backup first)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0set-prestige-level.ps1" %*
