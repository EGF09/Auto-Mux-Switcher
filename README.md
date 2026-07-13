# Auto MUX Switcher

Laptop fişten çıkarıldığında/takıldığında **dGPU'yu otomatik olarak devre dışı bırakma/etkinleştirme** yaparak pil tasarrufu sağlayan bir Windows uygulaması.

## 🔧 Nasıl Çalışır?

```
[Güç Değişikliği] → [Windows Service algılar] → [Tray uygulaması bildirir] → [Kullanıcı onaylar] → [dGPU açılır/kapanır]
```

1. **Fiş çıkarıldığında**: Service güç değişikliğini algılar → Tray uygulaması "dGPU devre dışı bırakılsın mı?" diye sorar → Evet: dGPU kapanır, pil tasarrufu sağlanır
2. **Fiş takıldığında**: Aynı mantıkla dGPU tekrar etkinleştirilir → Tam performans
3. **Bilgisayar kapalıyken fiş değişirse**: İlk açılışta durumu algılar ve bildirimi gösterir

## 📁 Proje Yapısı

| Bileşen | Konum | Görev |
|---------|-------|-------|
| **AutoMuxService** | `src/AutoMuxService/` | Windows Service — Güç izleme, GPU yönetimi |
| **AutoMuxTray** | `src/AutoMuxTray/` | System Tray — Kullanıcı bildirimi, GPU durum izleme |

## 🚀 Kurulum

### Gereksinimler
- Windows 10/11
- .NET 10 Runtime
- Yönetici (Administrator) yetkileri

### Kolay Kurulum (Önerilen)

1. **Projeyi Derle**
```powershell
dotnet publish src/AutoMuxService -c Release -o publish/service
dotnet publish src/AutoMuxTray -c Release -o publish/tray
```

2. **install.bat'ı Yönetici olarak çalıştır**
```
install.bat → Sağ tık → Yönetici olarak çalıştır
```

Bu script otomatik olarak:
- Dosyaları `C:\Program Files\AutoMuxSwitcher\` altına kopyalar
- Windows Service'i oluşturur ve başlatır (otomatik başlatma)
- Tray uygulamasını Windows başlangıcına ekler
- Her ikisini de hemen çalıştırır

> **Not:** Bilgisayar yeniden başlatıldığında Service ve Tray uygulaması otomatik olarak başlar.

### Manuel Kurulum

<details>
<summary>Manuel kurulum adımları (gelişmiş kullanıcılar için)</summary>

#### 1. Projeyi Derle
```powershell
dotnet publish src/AutoMuxService -c Release -o publish/service
dotnet publish src/AutoMuxTray -c Release -o publish/tray
```

#### 2. Windows Service Olarak Kur
```powershell
# Yönetici PowerShell'i proje klasöründe (Auto-Mux-Switcher) açıp çalıştırın:
$ServicePath = Join-Path $PWD "publish\service\AutoMuxService.exe"
sc.exe create AutoMuxSwitcher binPath= $ServicePath start= auto
sc.exe description AutoMuxSwitcher "Güç durumuna göre dGPU yönetimi - Auto MUX Switcher"
sc.exe start AutoMuxSwitcher
```

#### 3. Tray Uygulamasını Başlangıça Ekle
```powershell
# Başlangıç klasörüne kısayol oluştur
$WshShell = New-Object -ComObject WScript.Shell
$ShortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\AutoMuxTray.lnk"
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = Join-Path $PWD "publish\tray\AutoMuxTray.exe"
$Shortcut.Save()
```

</details>

## 🗑️ Kaldırma

### Kolay Kaldırma (Önerilen)

```
uninstall.bat → Sağ tık → Yönetici olarak çalıştır
```

Bu script otomatik olarak Service'i, başlangıç kısayolunu, kurulum dosyalarını ve Registry kayıtlarını temizler.

### Manuel Kaldırma

<details>
<summary>Manuel kaldırma adımları</summary>

```powershell
# Yönetici PowerShell'de
sc.exe stop AutoMuxSwitcher
sc.exe delete AutoMuxSwitcher

# Başlangıç kısayolunu sil
Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\AutoMuxTray.lnk"

# Registry'yi temizle
Remove-Item "HKLM:\SOFTWARE\AutoMuxSwitcher" -ErrorAction SilentlyContinue

# Kurulum dosyalarını sil (install.bat ile kurulduysa)
Remove-Item "C:\Program Files\AutoMuxSwitcher" -Recurse -Force
```

</details>

## ⚙️ Teknik Detaylar

- **Güç İzleme**: WMI (`Win32_PowerManagementEvent`) + P/Invoke polling (5 sn)
- **GPU Kontrolü**: `pnputil.exe /disable-device` ve `/enable-device`
- **GPU Durum İzleme**: WMI (`Win32_VideoController` + `Win32_PnPEntity`) — 30 sn periyodik kontrol
- **IPC**: Named Pipes (`AutoMuxSwitcherPipe`) — retry mekanizmalı
- **Durum Saklama**: Windows Registry (`HKLM\SOFTWARE\AutoMuxSwitcher`)
- **GPU Algılama**: WMI — NVIDIA/AMD otomatik algılama (devre dışı dahil)

## 📝 Lisans

MIT
