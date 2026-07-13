#Requires -RunAsAdministrator
# ============================================================
# Auto MUX Switcher - Kaldirma Scripti
#
# DIKKAT: Bu script YALNIZCA C:\Program Files\AutoMuxSwitcher
# klasorundeki kurulum dosyalarini siler. Proje kaynak kodlarina
# ASLA dokunmaz.
# ============================================================

$ErrorActionPreference = "Continue"

# Yapilandirma
$InstallDir   = "C:\Program Files\AutoMuxSwitcher"
$ServiceName  = "AutoMuxSwitcher"
$TrayExeName  = "AutoMuxTray"
$ShortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\AutoMuxTray.lnk"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Auto MUX Switcher - Kaldirma" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# Silinecekleri goster
Write-Host "Silinecek ogeler:" -ForegroundColor Yellow
Write-Host "  - Windows Service: $ServiceName" -ForegroundColor White
Write-Host "  - Baslangic kisayolu: $ShortcutPath" -ForegroundColor White
Write-Host "  - Kurulum dizini: $InstallDir" -ForegroundColor White
Write-Host "  - Registry: HKLM\SOFTWARE\AutoMuxSwitcher" -ForegroundColor White
Write-Host ""
Write-Host "NOT: Proje kaynak kodlarina dokunulmayacak." -ForegroundColor Green
Write-Host ""

$confirm = Read-Host "Devam etmek istiyor musunuz? (E/H)"
if ($confirm -ne "E" -and $confirm -ne "e") {
    Write-Host ""
    Write-Host "Kaldirma iptal edildi." -ForegroundColor Yellow
    exit 0
}

Write-Host ""

# 1. Tray uygulamasini kapat
Write-Host "[1/5] Tray uygulamasi kapatiliyor..." -ForegroundColor White
$trayProcess = Get-Process -Name $TrayExeName -ErrorAction SilentlyContinue
if ($trayProcess) {
    Stop-Process -Name $TrayExeName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-Host "       Tray uygulamasi kapatildi." -ForegroundColor Green
} else {
    Write-Host "       Tray uygulamasi zaten calismiyordu." -ForegroundColor Gray
}

# 2. Service'i durdur ve sil
Write-Host "[2/5] Windows Service kaldiriliyor..." -ForegroundColor White
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($existingService.Status -eq "Running") {
        sc.exe stop $ServiceName | Out-Null
        Start-Sleep -Seconds 3
    }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "       Service kaldirildi." -ForegroundColor Green
} else {
    Write-Host "       Service bulunamadi (zaten kaldirilmis)." -ForegroundColor Gray
}

# Eski isimle kalmis service'leri de temizle (Kalıntı temizligi)
$legacyServiceName = "AutoMuxSwitcherService"
$legacyService = Get-Service -Name $legacyServiceName -ErrorAction SilentlyContinue
if ($legacyService) {
    if ($legacyService.Status -eq "Running") {
        sc.exe stop $legacyServiceName | Out-Null
        Start-Sleep -Seconds 3
    }
    sc.exe delete $legacyServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# 3. Baslangic kisayolunu sil
Write-Host "[3/5] Baslangic kisayolu kaldiriliyor..." -ForegroundColor White
if (Test-Path $ShortcutPath) {
    Remove-Item -Path $ShortcutPath -Force
    Write-Host "       Baslangic kisayolu silindi." -ForegroundColor Green
} else {
    Write-Host "       Baslangic kisayolu bulunamadi." -ForegroundColor Gray
}

# 4. SADECE Program Files kurulum dosyalarini sil
Write-Host "[4/5] Kurulum dosyalari siliniyor..." -ForegroundColor White
if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    
    if (Test-Path $InstallDir) {
        Write-Host "[UYARI] Bazi dosyalar silinemedi." -ForegroundColor Yellow
        Write-Host "        Bilgisayari yeniden baslattiktan sonra" -ForegroundColor Yellow
        Write-Host "        $InstallDir klasorunu manuel olarak silebilirsiniz." -ForegroundColor Yellow
    } else {
        Write-Host "       Kurulum dosyalari silindi." -ForegroundColor Green
    }
} else {
    Write-Host "       Kurulum dizini bulunamadi ($InstallDir)." -ForegroundColor Gray
}

# 5. Registry'yi temizle
Write-Host "[5/5] Registry kayitlari temizleniyor..." -ForegroundColor White
Remove-Item -Path "HKLM:\SOFTWARE\AutoMuxSwitcher" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\SOFTWARE\AutoMuxSwitcher" -Force -ErrorAction SilentlyContinue
Write-Host "       Registry temizlendi." -ForegroundColor Green

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Kaldirma tamamlandi!" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Auto MUX Switcher basariyla kaldirildi." -ForegroundColor White
Write-Host "  Proje kaynak kodlariniz dokunulmadan korunmustur." -ForegroundColor White
Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
