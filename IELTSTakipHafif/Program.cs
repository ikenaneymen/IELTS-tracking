using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// --------------------------------------------------
// AYARLAR
// --------------------------------------------------

const string TakipEdilecekSayfa =
    "http://prep.bilkent.edu.tr/ielts-kayit/";

string ntfyTopic =
    Environment.GetEnvironmentVariable("NTFY_TOPIC")
    ?? "ielts-bizziko-1712260705";

// 5 saniye
TimeSpan kontrolAraligi =
    TimeSpan.FromSeconds(5);

// --once kullanılmışsa yalnızca bir kere kontrol eder.
// GitHub Actions daha sonra bu modu kullanacak.
bool tekKontrolModu = args.Any(
    arg => arg.Equals(
        "--once",
        StringComparison.OrdinalIgnoreCase));

// state.json çalıştırılan klasörde oluşur.
string durumDosyasi =
    Path.Combine(
        Environment.CurrentDirectory,
        "state.json");


// --------------------------------------------------
// PROGRAM BAŞLANGICI
// --------------------------------------------------

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("Bilkent IELTS takip programı başlatıldı.");
Console.WriteLine(
    tekKontrolModu
        ? "Çalışma modu: Tek kontrol"
        : "Çalışma modu: Sürekli takip");

Console.WriteLine($"Durum dosyası: {durumDosyasi}");
Console.WriteLine();

using HttpClient httpClient = new();

httpClient.Timeout =
    TimeSpan.FromSeconds(30);

httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Mozilla/5.0 IELTS-Takip-Botu/2.0");

while (true)
{
    try
    {
        SinavBilgisi? mevcutSinav =
            await SinavBilgisiniGetir(
                httpClient,
                TakipEdilecekSayfa);

        if (mevcutSinav is null)
        {
            Console.WriteLine(
                $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} - " +
                "IELTS sınav bağlantısı bulunamadı.");
        }
        else
        {
            SinavBilgisi? oncekiSinav =
                await DurumuOku(durumDosyasi);

            if (oncekiSinav is null)
            {
                await DurumuKaydet(
                    durumDosyasi,
                    mevcutSinav);

                Console.WriteLine(
                    $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} - " +
                    "İlk çalışma: Mevcut sınav başlangıç olarak kaydedildi.");

                Console.WriteLine(
                    $"Sınav: {mevcutSinav.Yazi}");
            }
            else if (SinavDegisti(
                         oncekiSinav,
                         mevcutSinav))
            {
                Console.WriteLine();
                Console.WriteLine(
                    "========================================");

                Console.WriteLine(
                    "YENİ IELTS SINAVI TESPİT EDİLDİ!");

                Console.WriteLine(
                    $"Eski sınav: {oncekiSinav.Yazi}");

                Console.WriteLine(
                    $"Yeni sınav: {mevcutSinav.Yazi}");

                Console.WriteLine(
                    $"Yeni bağlantı: {mevcutSinav.Link}");

                Console.WriteLine(
                    $"Tespit zamanı: " +
                    $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}");

                Console.WriteLine(
                    "========================================");

                string kaynak =
                    tekKontrolModu
                        ? "GitHub yedeği"
                        : "Bilgisayar";

                bool bildirimGonderildi =
                    await BildirimGonder(
                        httpClient,
                        ntfyTopic,
                        $"Yeni Bilkent IELTS Sınavı — {kaynak}",
                        $"Eski sınav:\n" +
                        $"{oncekiSinav.Yazi}\n\n" +
                        $"Yeni sınav:\n" +
                        $"{mevcutSinav.Yazi}\n\n" +
                        $"Tespit zamanı:\n" +
                        $"{DateTime.Now:dd.MM.yyyy HH:mm:ss}",
                        mevcutSinav.Link);

                if (bildirimGonderildi)
                {
                    await DurumuKaydet(
                        durumDosyasi,
                        mevcutSinav);

                    Console.WriteLine(
                        "Yeni durum kaydedildi.");

                    if (!tekKontrolModu &&
                        OperatingSystem.IsWindows())
                    {
                        Console.Beep(1000, 400);
                        Console.Beep(1300, 400);
                        Console.Beep(1600, 800);
                    }
                }
                else
                {
                    Console.WriteLine(
                        "Bildirim gönderilemediği için durum dosyası " +
                        "güncellenmedi.");

                    Console.WriteLine(
                        "Bir sonraki kontrolde yeniden denenecek.");
                }
            }
            else
            {
                Console.WriteLine(
                    $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} - " +
                    $"Değişiklik yok: {mevcutSinav.Yazi}");
            }
        }
    }
    catch (TaskCanceledException)
    {
        Console.WriteLine(
            $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} - " +
            "İstek zaman aşımına uğradı.");
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine(
            $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} - " +
            $"Bağlantı hatası: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} - " +
            $"Beklenmeyen hata: {ex.Message}");
    }

    if (tekKontrolModu)
    {
        Console.WriteLine();
        Console.WriteLine("Tek kontrol tamamlandı.");
        break;
    }

    await Task.Delay(kontrolAraligi);
}


// --------------------------------------------------
// SAYFADAN SINAV BİLGİSİNİ AL
// --------------------------------------------------

async Task<SinavBilgisi?> SinavBilgisiniGetir(
    HttpClient client,
    string sayfaAdresi)
{
    string html =
        await client.GetStringAsync(sayfaAdresi);

    Regex linkDeseni = new(
        @"<a\b[^>]*href\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase |
        RegexOptions.Singleline);

    MatchCollection linkler =
        linkDeseni.Matches(html);

    foreach (Match link in linkler)
    {
        string hamYazi =
            link.Groups["text"].Value;

        string temizYazi =
            Regex.Replace(
                hamYazi,
                "<.*?>",
                string.Empty);

        temizYazi =
            WebUtility
                .HtmlDecode(temizYazi)
                .Trim();

        temizYazi =
            Regex.Replace(
                temizYazi,
                @"\s+",
                " ");

        if (!temizYazi.Contains(
                "IELTS Sınavı",
                StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        string href =
            WebUtility.HtmlDecode(
                link.Groups["href"]
                    .Value
                    .Trim());

        Uri anaSayfa =
            new(sayfaAdresi);

        Uri tamLink =
            new(anaSayfa, href);

        return new SinavBilgisi
        {
            Yazi = temizYazi,
            Link = tamLink.ToString()
        };
    }

    return null;
}


// --------------------------------------------------
// DEĞİŞİKLİK KONTROLÜ
// --------------------------------------------------

bool SinavDegisti(
    SinavBilgisi eskiSinav,
    SinavBilgisi yeniSinav)
{
    bool yaziDegisti =
        !string.Equals(
            eskiSinav.Yazi,
            yeniSinav.Yazi,
            StringComparison.Ordinal);

    bool linkDegisti =
        !string.Equals(
            eskiSinav.Link,
            yeniSinav.Link,
            StringComparison.Ordinal);

    return yaziDegisti || linkDegisti;
}


// --------------------------------------------------
// DURUM DOSYASINI OKU
// --------------------------------------------------

async Task<SinavBilgisi?> DurumuOku(
    string dosyaYolu)
{
    if (!File.Exists(dosyaYolu))
    {
        return null;
    }

    try
    {
        string json =
            await File.ReadAllTextAsync(
                dosyaYolu,
                Encoding.UTF8);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<SinavBilgisi>(
            json);
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"Durum dosyası okunamadı: {ex.Message}");

        return null;
    }
}


// --------------------------------------------------
// DURUM DOSYASINI KAYDET
// --------------------------------------------------

async Task DurumuKaydet(
    string dosyaYolu,
    SinavBilgisi sinav)
{
    JsonSerializerOptions ayarlar = new()
    {
        WriteIndented = true
    };

    string json =
        JsonSerializer.Serialize(
            sinav,
            ayarlar);

    await File.WriteAllTextAsync(
        dosyaYolu,
        json,
        Encoding.UTF8);
}


// --------------------------------------------------
// NTFY BİLDİRİMİ GÖNDER
// --------------------------------------------------

async Task<bool> BildirimGonder(
    HttpClient client,
    string topic,
    string baslik,
    string mesaj,
    string tiklanacakLink)
{
    try
    {
        var bildirim = new
        {
            topic,
            title = baslik,
            message = mesaj,
            priority = 5,
            tags = new[]
            {
                "rotating_light",
                "calendar"
            },
            click = tiklanacakLink
        };

        string json =
            JsonSerializer.Serialize(bildirim);

        using StringContent content = new(
            json,
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response =
            await client.PostAsync(
                "https://ntfy.sh",
                content);

        if (response.IsSuccessStatusCode)
        {
            Console.WriteLine(
                "Telefon bildirimi başarıyla gönderildi.");

            return true;
        }

        string cevap =
            await response.Content
                .ReadAsStringAsync();

        Console.WriteLine(
            $"Bildirim gönderilemedi: " +
            $"{(int)response.StatusCode} " +
            $"{response.ReasonPhrase}");

        Console.WriteLine(cevap);

        return false;
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"Bildirim hatası: {ex.Message}");

        return false;
    }
}


// --------------------------------------------------
// SINAV BİLGİSİ MODELİ
// --------------------------------------------------

class SinavBilgisi
{
    public string Yazi { get; set; } = "";

    public string Link { get; set; } = "";
}