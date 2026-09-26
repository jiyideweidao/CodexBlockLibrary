@echo off
title Codex Block Library - Install
cls
echo.
echo   Codex Block Library for AutoCAD - Installer
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\install.ps1"
set RC=%ERRORLEVEL%
echo.
if "%RC%"=="0" (echo   [Done] Installation finished.) else (echo   [Failed] Exit code = %RC%)
echo.
pause