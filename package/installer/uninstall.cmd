@echo off
title Codex Block Library - Uninstall
cls
echo.
echo   Codex Block Library for AutoCAD - Uninstaller
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\uninstall.ps1"
set RC=%ERRORLEVEL%
echo.
if "%RC%"=="0" (echo   [Done] Uninstall finished.) else (echo   [Failed] Exit code = %RC%)
echo.
pause