@echo off
rem One-click GitHub hosts fix. Requests admin via UAC, then runs tools\github-hosts.ps1
chcp 65001 >nul
title Warmspin - GitHub access fix
echo.
echo   Requesting administrator privileges...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','%~dp0tools\github-hosts.ps1'"
