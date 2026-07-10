namespace AutoMuxTray;

/// <summary>
/// Kullanıcıya bildirim gösterme işlemlerini yönetir.
/// Service'den gelen güç değişikliği mesajlarına göre uygun dialog gösterir.
/// </summary>
public class NotificationManager
{
    /// <summary>
    /// Fiş çıkarıldığında gösterilecek dialog.
    /// Kullanıcıya dGPU'yu devre dışı bırakma seçeneği sunar.
    /// </summary>
    /// <param name="isStartup">Bilgisayar kapalıyken değişim olduysa true</param>
    /// <returns>true = kullanıcı Evet dedi, false = Hayır</returns>
    public bool ShowUnpluggedNotification(bool isStartup = false)
    {
        var message = isStartup
            ? "Bilgisayarınız kapalıyken fişten çıkartılmış.\n\n" +
              "Pil tasarrufu için ekran kartı (dGPU) devre dışı bırakılsın mı?"
            : "Bilgisayarınız fişten çıkartıldı.\n\n" +
              "Pil tasarrufu için ekran kartı (dGPU) devre dışı bırakılsın mı?";

        var result = MessageBox.Show(
            message,
            "Auto MUX Switcher — Güç Değişikliği",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.DefaultDesktopOnly); // Ön plana gelsin

        return result == DialogResult.Yes;
    }

    /// <summary>
    /// Fiş takıldığında gösterilecek dialog.
    /// Kullanıcıya dGPU'yu etkinleştirme seçeneği sunar.
    /// </summary>
    /// <param name="isStartup">Bilgisayar kapalıyken değişim olduysa true</param>
    /// <returns>true = kullanıcı Evet dedi, false = Hayır</returns>
    public bool ShowPluggedInNotification(bool isStartup = false)
    {
        var message = isStartup
            ? "Bilgisayarınız kapalıyken fişe takılmış.\n\n" +
              "Performans için ekran kartı (dGPU) etkinleştirilsin mi?"
            : "Bilgisayarınız fişe takıldı.\n\n" +
              "Performans için ekran kartı (dGPU) etkinleştirilsin mi?";

        var result = MessageBox.Show(
            message,
            "Auto MUX Switcher — Güç Değişikliği",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1,
            MessageBoxOptions.DefaultDesktopOnly);

        return result == DialogResult.Yes;
    }

    /// <summary>
    /// İşlem sonucu bildirimi gösterir.
    /// </summary>
    /// <param name="notifyIcon">Tray ikonu (balloon notification için)</param>
    /// <param name="success">İşlem başarılı mı?</param>
    /// <param name="isEnabled">GPU etkinleştirildi mi, devre dışı mı?</param>
    public void ShowResultNotification(NotifyIcon notifyIcon, bool success, bool isEnabled)
    {
        if (success)
        {
            var title = "Auto MUX Switcher";
            var text = isEnabled
                ? "✅ Ekran kartı (dGPU) etkinleştirildi."
                : "✅ Ekran kartı (dGPU) devre dışı bırakıldı. Pil tasarrufu aktif.";

            notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
        }
        else
        {
            notifyIcon.ShowBalloonTip(
                3000,
                "Auto MUX Switcher — Hata",
                "❌ İşlem gerçekleştirilemedi. Detaylar için log dosyasını kontrol edin.",
                ToolTipIcon.Error);
        }
    }

    /// <summary>
    /// Hata mesajı gösterir.
    /// </summary>
    public void ShowError(NotifyIcon notifyIcon, string errorMessage)
    {
        notifyIcon.ShowBalloonTip(
            3000,
            "Auto MUX Switcher — Hata",
            errorMessage,
            ToolTipIcon.Error);
    }
}
