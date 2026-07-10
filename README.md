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
| **AutoMuxTray** | `src/AutoMuxTray/` | System Tray — Kullanıcı bildirimi ve etkileşim |

## 🚀 Kurulum

### Gereksinimler
- Windows 10/11
- .NET 10 Runtime
- Yönetici (Administrator) yetkileri

### 1. Projeyi Derle
```powershell
dotnet publish src/AutoMuxService -c Release -o publish/service
dotnet publish src/AutoMuxTray -c Release -o publish/tray
```

### 2. Windows Service Olarak Kur
```powershell
# Yönetici PowerShell'de çalıştır
sc.exe create AutoMuxSwitcher binPath="C:\tam\yol\publish\service\AutoMuxService.exe" start=auto
sc.exe description AutoMuxSwitcher "Güç durumuna göre dGPU yönetimi - Auto MUX Switcher"
sc.exe start AutoMuxSwitcher
```

### 3. Tray Uygulamasını Başlangıça Ekle
Tray uygulamasının kullanıcı oturum açıldığında otomatik başlaması için:

```powershell
# Başlangıç klasörüne kısayol oluştur
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\AutoMuxTray.lnk")
$Shortcut.TargetPath = "C:\tam\yol\publish\tray\AutoMuxTray.exe"
$Shortcut.Save()
```

## 🗑️ Kaldırma

```powershell
# Yönetici PowerShell'de
sc.exe stop AutoMuxSwitcher
sc.exe delete AutoMuxSwitcher

# Başlangıç kısayolunu sil
Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\AutoMuxTray.lnk"

# Registry'yi temizle
Remove-Item "HKLM:\SOFTWARE\AutoMuxSwitcher" -ErrorAction SilentlyContinue
```

## ⚙️ Teknik Detaylar

- **Güç İzleme**: WMI (`Win32_PowerManagementEvent`) + P/Invoke polling (5 sn)
- **GPU Kontrolü**: `pnputil.exe /disable-device` ve `/enable-device`
- **IPC**: Named Pipes (`AutoMuxSwitcherPipe`)
- **Durum Saklama**: Windows Registry (`HKLM\SOFTWARE\AutoMuxSwitcher`)
- **GPU Algılama**: WMI (`Win32_VideoController`) — NVIDIA/AMD otomatik algılama

## 📝 Lisans

MIT