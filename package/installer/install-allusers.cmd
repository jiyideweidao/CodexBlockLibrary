@echo off
title Codex Block Library - Install (All Users)
cls
echo.
echo   Codex Block Library for AutoCAD - Installer (All Users)
echo   A UAC prompt will appear because administrator rights are required.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install.ps1" -AllUsers
echo.
echo   If a UAC window was accepted, installation continues in the new window.
echo.
pause