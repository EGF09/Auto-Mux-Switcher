#Requires -RunAsAdministrator
# ============================================================
# Auto MUX Switcher - Kurulum Scripti
# ============================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Yapilandirma
$InstallDir   = "C:\Program Files\AutoMuxSwitcher"
$ServiceDir   = "$InstallDir\service"
$TrayDir      = "$InstallDir\tray"
$ServiceName  = "AutoMuxSwitcher"
$TrayExeName  = "AutoMuxTray.exe"
$SourceService = Join-Path $ScriptDir "publish\service"
$SourceTray    = Join-Path $ScriptDir "publish\tray"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Auto MUX Switcher - Kurulum" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Kaynak dosyalari kontrol et
if (-not (Test-Path "$SourceService\AutoMuxService.exe")) {
    Write-Host "[HATA] publish\service\AutoMuxService.exe bulunamadi!" -ForegroundColor Red
    Write-Host "       Once projeyi derleyin:" -ForegroundColor Yellow
    Write-Host "       dotnet publish src/AutoMuxService -c Release -o publish/service" -ForegroundColor Yellow
    Write-Host "       dotnet publish src/AutoMuxTray -c Release -o publish/tray" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path "$SourceTray\AutoMuxTray.exe")) {
    Write-Host "[HATA] publish\tray\AutoMuxTray.exe bulunamadi!" -ForegroundColor Red
    Write-Host "       Once projeyi derleyin:" -ForegroundColor Yellow
    Write-Host "       dotnet publish src/AutoMuxService -c Release -o publish/service" -ForegroundColor Yellow
    Write-Host "       dotnet publish src/AutoMuxTray -c Release -o publish/tray" -ForegroundColor Yellow
    exit 1
}

# 2. Mevcut kurulumu temizle
Write-Host "[1/6] Mevcut kurulum kontrol ediliyor..." -ForegroundColor White

# Tray uygulamasini kapat
$trayProcess = Get-Process -Name "AutoMuxTray" -ErrorAction SilentlyContinue
if ($trayProcess) {
    Write-Host "       Tray uygulamasi kapatiliyor..." -ForegroundColor Gray
    Stop-Process -Name "AutoMuxTray" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

# Mevcut service'i kontrol et ve kaldir
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "[2/6] Mevcut Service durduruluyor ve kaldiriliyor..." -ForegroundColor White
    
    if ($existingService.Status -eq "Running") {
        Write-Host "       Service durduruluyor..." -ForegroundColor Gray
        sc.exe stop $ServiceName | Out-Null
        Start-Sleep -Seconds 3
    }
    
    Write-Host "       Service siliniyor..." -ForegroundColor Gray
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    
    Write-Host "       Eski service kaldirildi." -ForegroundColor Green
} else {
    Write-Host "[2/6] Mevcut service bulunamadi (yeni kurulum)." -ForegroundColor Gray
}

# 3. Kurulum dizinlerini olustur
Write-Host "[3/6] Kurulum dizini hazirlaniyor..." -ForegroundColor White

if (Test-Path $InstallDir) {
    Write-Host "       Eski kurulum dosyalari temizleniyor..." -ForegroundColor Gray
    Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}

New-Item -ItemType Directory -Path $ServiceDir -Force | Out-Null
New-Item -ItemType Directory -Path $TrayDir -Force | Out-Null

# 4. Dosyalari kopyala
Write-Host "[4/6] Dosyalar kopyalaniyor..." -ForegroundColor White

Write-Host "       Service dosyalari kopyalaniyor..." -ForegroundColor Gray
Copy-Item -Path "$SourceService\*" -Destination $ServiceDir -Recurse -Force
Write-Host "       Tray dosyalari kopyalaniyor..." -ForegroundColor Gray
Copy-Item -Path "$SourceTray\*" -Destination $TrayDir -Recurse -Force

Write-Host "       Dosyalar basariyla kopyalandi." -ForegroundColor Green

# 5. Windows Service olustur ve baslat
Write-Host "[5/6] Windows Service olusturuluyor..." -ForegroundColor White

$serviceBinPath = "`"$ServiceDir\AutoMuxService.exe`""
$createResult = sc.exe create $ServiceName binPath= $serviceBinPath start= auto DisplayName= "Auto MUX Switcher" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[HATA] Service olusturulamadi: $createResult" -ForegroundColor Red
    exit 1
}

sc.exe description $ServiceName "Guc durumuna gore dGPU otomatik yonetimi - Pil tasarrufu icin ekran kartini otomatik acar/kapatir." | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

Write-Host "       Service olusturuldu." -ForegroundColor Green

# Service'i baslat
Write-Host "       Service baslatiliyor..." -ForegroundColor Gray
$startResult = sc.exe start $ServiceName 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "       Service basariyla baslatildi." -ForegroundColor Green
} else {
    Write-Host "[UYARI] Service baslatilamadi. Yeniden baslatmada otomatik baslayacak." -ForegroundColor Yellow
    Write-Host "       Detay: $startResult" -ForegroundColor Gray
}

# 6. Tray uygulamasini baslangica ekle ve baslat
Write-Host "[6/6] Tray uygulamasi ayarlaniyor..." -ForegroundColor White

$startupDir = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs\Startup")
$shortcutPath = Join-Path $startupDir "AutoMuxTray.lnk"
$trayExePath = Join-Path $TrayDir $TrayExeName

try {
    $WshShell = New-Object -ComObject WScript.Shell
    $shortcut = $WshShell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $trayExePath
    $shortcut.WorkingDirectory = $TrayDir
    $shortcut.Description = "Auto MUX Switcher - System Tray"
    $shortcut.Save()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($WshShell) | Out-Null
    Write-Host "       Baslangic kisayolu olusturuldu." -ForegroundColor Green
} catch {
    Write-Host "[UYARI] Baslangic kisayolu olusturulamadi: $_" -ForegroundColor Yellow
}

# Tray uygulamasini baslat
Write-Host "       Tray uygulamasi baslatiliyor..." -ForegroundColor Gray
Start-Process -FilePath $trayExePath -WorkingDirectory $TrayDir

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Kurulum tamamlandi!" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Service durumu  : Otomatik baslatma (auto)" -ForegroundColor White
Write-Host "  Kurulum dizini  : $InstallDir" -ForegroundColor White
Write-Host "  Tray uygulamasi : Windows baslangicina eklendi" -ForegroundColor White
Write-Host ""
Write-Host "  Kaldirmak icin  : uninstall.bat dosyasini Yonetici olarak calistirin" -ForegroundColor White
Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""

# Dogrulama
Write-Host "Dogrulama:" -ForegroundColor Cyan
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "  Service: $($svc.Status)" -ForegroundColor $(if ($svc.Status -eq "Running") { "Green" } else { "Yellow" })
} else {
    Write-Host "  Service: Bulunamadi!" -ForegroundColor Red
}

$tray = Get-Process -Name "AutoMuxTray" -ErrorAction SilentlyContinue
if ($tray) {
    Write-Host "  Tray   : Calisiyor (PID: $($tray.Id))" -ForegroundColor Green
} else {
    Write-Host "  Tray   : Calismadi!" -ForegroundColor Red
}
Write-Host ""
