@echo off
:: ============================================================
:: Auto MUX Switcher - Kaldirma
:: Bu dosya uninstall.ps1'i yonetici olarak calistirir.
:: ============================================================

:: Yonetici kontrolu
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo Bu script Yonetici olarak calistirilmalidir!
    echo Sag tiklayip "Yonetici olarak calistir" secenegini kullanin.
    echo.
    pause
    exit /b 1
)

:: PowerShell scriptini calistir
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
pause
