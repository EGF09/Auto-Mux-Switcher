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
| **AutoMuxSetup** | `src/AutoMuxSetup/` | Kurulum Aracı — Yükle, Onar, Kaldır (WinForms GUI) |

### Desteklenen GPU'lar
- **NVIDIA** (GeForce, Quadro, RTX serisi)
- **AMD** discrete (Radeon RX, Radeon Pro serisi)
- AMD Radeon entegre (APU) ve Intel iGPU otomatik olarak atlanır

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
   dotnet publish src/AutoMuxSetup -c Release -o publish/setup
   ```

2. **`AutoMuxSetup.exe`'yi çalıştır**
   ```
   publish/setup/AutoMuxSetup.exe → Çift tık (otomatik yönetici izni ister)
   ```

3. **"YÜKLE" butonuna tıkla**

   Setup aracı `publish/` dizinindeki `service/` ve `tray/` klasörlerini otomatik olarak bulur ve kurulumu gerçekleştirir.

Kurulum sırasında otomatik olarak:
- Eski kurulumu algılar ve temizler
- Dosyaları `C:\Program Files\AutoMuxSwitcher\` altına kopyalar
- Windows Service'i oluşturur ve başlatır (otomatik başlatma)
- Service hata kurtarma politikası ayarlar (otomatik yeniden başlatma)
- Tray uygulamasını Windows başlangıcına ekler
- Her ikisini de hemen çalıştırır
- Sonunda doğrulama yapar ve sonucu raporlar

> **Not:** Bilgisayar yeniden başlatıldığında Service ve Tray uygulaması otomatik olarak başlar.

### Setup Aracı Özellikleri

| Buton | İşlev |
|-------|-------|
| **⬇ YÜKLE** | Tam kurulum yapar. Eski kurulum varsa önce temizler, sonra yeniden kurar. |
| **🔧 ONAR** | Mevcut kurulumu onarır — dosyaları günceller, servisi yeniden başlatır. |
| **✖ KALDIR** | Uygulamayı sistemden tamamen kaldırır. |

Setup aracı koyu temalı modern bir arayüze sahiptir ve işlem ilerleme çubuğu ile detaylı log çıktısı gösterir.

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

`AutoMuxSetup.exe`'yi çalıştır → **"KALDIR"** butonuna tıkla.

Setup aracı **yalnızca** kurulum dosyalarını kaldırır:
- `C:\Program Files\AutoMuxSwitcher\` dizinini siler
- Windows Service'i kaldırır
- Başlangıç kısayolunu siler
- Registry kayıtlarını temizler

> **Proje kaynak kodlarına dokunmaz.**

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

# Kurulum dosyalarını sil
Remove-Item "C:\Program Files\AutoMuxSwitcher" -Recurse -Force
```

</details>

## 📦 Dosya Yapısı

```
Auto-Mux-Switcher/
├── README.md
├── AutoMuxSwitcher.slnx
├── src/
│   ├── AutoMuxService/  # Windows Service kaynak kodu
│   ├── AutoMuxTray/     # System Tray kaynak kodu
│   └── AutoMuxSetup/    # Kurulum Aracı kaynak kodu (WinForms)
└── publish/
    ├── service/         # Derlenmiş Service dosyaları
    ├── tray/            # Derlenmiş Tray dosyaları
    └── setup/           # Derlenmiş Setup dosyaları
```

## ⚙️ Teknik Detaylar

- **Güç İzleme**: WMI (`Win32_PowerManagementEvent`) + P/Invoke polling (5 sn)
- **GPU Kontrolü**: `pnputil.exe /disable-device` ve `/enable-device`
- **GPU Durum İzleme**: WMI (`Win32_VideoController` + `Win32_PnPEntity`) — 30 sn periyodik kontrol
- **IPC**: Named Pipes (`AutoMuxSwitcherPipe`) — 3 denemeye kadar yeniden deneme
- **Durum Saklama**: Windows Registry (`HKLM\SOFTWARE\AutoMuxSwitcher`)
- **GPU Algılama**: WMI — NVIDIA/AMD otomatik algılama (Intel ve AMD APU iGPU atlanır)
- **Kurulum Aracı**: WinForms GUI — `sc.exe` ile Service yönetimi, PowerShell COM ile kısayol oluşturma

## 📝 Lisans

MIT
