# Resmî Gazete Günlük Mail Botu (C#)

Bu proje her çalıştırıldığında resmigazete.gov.tr'nin resmi RSS beslemesini
(`https://www.resmigazete.gov.tr/rss`) okur, o güne ait başlıkları HTML formatında
bir e-postaya dönüştürür ve SMTP üzerinden gönderir. Günlük gönderim, Windows
Görev Zamanlayıcı (veya Linux'ta cron/systemd timer) ile sağlanır — .NET
uygulamasının kendisi sürekli çalışan bir servis değildir, sadece
"çalıştır → gönder → kapan" mantığıyla çalışır. Bu, sürekli açık kalan bir
servis yazmaktan çok daha basit ve güvenilirdir.

## Önce şunu bilin: robots.txt ve kullanım sıklığı

resmigazete.gov.tr'nin robots.txt dosyası, otomatik/toplu tarama (crawling) araçlarını
kısıtlıyor. Bu proje siteyi taramıyor, sadece resmi olarak yayınlanan **tek bir RSS
adresini günde bir kez** okuyor; yine de kuruma ait bir kamu sitesi olduğu için:

- İsteği günde 1 kere (örn. sabah 09:00) ile sınırlı tutun, sık sık (dakikada bir vb.) çağırmayın.
- HTTP isteklerinde kendinizi tanıtan bir `User-Agent` gönderin (kodda zaten var).
- RSS yerine sayfaların HTML'ini kazımanız (scraping) gerekirse aynı kurallara uyun.
- Ticari/toplu dağıtım amaçlı kullanacaksanız önce sitenin kullanım şartlarını kontrol edin.

## 1. Gereksinimler

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Bir SMTP hesabı (Gmail, Outlook, kurumsal SMTP vb.)
  - Gmail kullanacaksanız normal şifre değil, **Uygulama Şifresi (App Password)**
    üretmeniz gerekir: Google Hesabı → Güvenlik → 2 Adımlı Doğrulama açık olmalı →
    "Uygulama şifreleri" bölümünden yeni bir şifre oluşturun.

## 2. Proje dosyaları

Bu klasördeki 3 dosya projenin tamamıdır:

```
ResmiGazeteMailer/
├── ResmiGazeteMailer.csproj   → proje tanımı ve NuGet paketleri
├── Program.cs                 → tüm mantık (RSS oku, e-posta oluştur, gönder)
└── appsettings.json           → SMTP ve RSS ayarları
```

Sıfırdan oluşturmak isterseniz aynı yapıyı şu komutla kurabilirsiniz:

```bash
dotnet new console -n ResmiGazeteMailer
cd ResmiGazeteMailer
dotnet add package MailKit
dotnet add package System.ServiceModel.Syndication
```

sonra `Program.cs` ve `appsettings.json` içeriklerini buradaki dosyalarla değiştirin.

## 3. Ayarları düzenleyin

`appsettings.json` içindeki alanları kendi bilgilerinizle doldurun:

```json
{
  "Rss": { "Url": "https://www.resmigazete.gov.tr/rss" },
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "UseSsl": true,
    "User": "gonderen.adresiniz@gmail.com",
    "Password": "16-haneli-uygulama-sifresi",
    "From": "gonderen.adresiniz@gmail.com",
    "FromName": "Resmi Gazete Bildirim",
    "To": [ "alici1@example.com", "alici2@example.com" ]
  }
}
```

> Şifreyi doğrudan appsettings.json içine yazmak yerine, gerçek bir kurulumda
> ortam değişkeni (`SMTP_PASSWORD` gibi) veya Windows Kimlik Bilgisi Yöneticisi /
> `dotnet user-secrets` kullanmanız önerilir. Basitlik için burada dosyada tutuyoruz.

## 3.5. Playwright tarayıcı motorunu kurun (yeni adım)

Bu proje artık siteyi düz bir HTTP isteğiyle değil, arka planda çalışan gerçek bir
Chromium tarayıcısıyla (Playwright) ziyaret ediyor — çünkü resmigazete.gov.tr,
tarayıcı olmayan istemcileri (curl, düz HttpClient) TLS bağlantı imzasından
tanıyıp engelliyor. Bunun için bir kerelik ek bir kurulum adımı gerekiyor:

```bash
cd ResmiGazeteMailer
dotnet build
dotnet tool install --global Microsoft.Playwright.CLI
playwright install chromium
```

`dotnet tool install` komutu zaten kuruluysa "already installed" uyarısı verir,
göz ardı edebilirsiniz. `playwright install chromium` komutu ~300 MB'lık bir
Chromium kopyasını indirir (bir kereye mahsus); bu indirme bittikten sonra
`dotnet run` normal şekilde çalışır.

Eğer `playwright` komutu terminalde tanınmıyor derse, dotnet global tool'ların
PATH'e eklenmesi gerekebilir — terminali kapatıp yeniden açmayı deneyin, ya da:

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

satırını `~/.zshrc` dosyanıza ekleyip terminali yeniden başlatın.

## 4. Çalıştırıp test edin

```bash
cd ResmiGazeteMailer
dotnet run
```

Konsolda "Tamamlandı: N öğe içeren e-posta gönderildi." mesajını görürseniz
hedef adrese e-posta ulaşmış demektir. Hata alırsanız:

- `Authentication failed` → SMTP kullanıcı adı/şifre (Gmail'de uygulama şifresi) yanlış.
- `Could not connect` → Port/SSL ayarı yanlış ya da güvenlik duvarı engelliyor.
- "Bugüne ait öğe bulunamadı" → RSS'te tarih alanı boş gelmiş olabilir; kod bu
  durumda otomatik olarak feed'deki tüm öğeleri gönderir, tekrar deneyin.

## 5. Yayına alma (publish)

Görev zamanlayıcı ile çalıştırmadan önce tek bir çalıştırılabilir dosya üretin:

```bash
dotnet publish -c Release -r win-x64 --self-contained false -o ./yayin
```

Bu, `./yayin` klasörüne `ResmiGazeteMailer.exe` ve `appsettings.json` kopyalar.

## 6. Windows Görev Zamanlayıcı ile günlük çalıştırma

### Arayüzden:

1. Başlat → "Görev Zamanlayıcı" (Task Scheduler) açın.
2. Sağdaki panelden **Temel Görev Oluştur**'a tıklayın.
3. İsim: `ResmiGazeteGunlukMail`, İleri.
4. Tetikleyici: **Günlük**, İleri → başlangıç saatini (örn. 09:00) girin.
5. Eylem: **Bir program başlat**, İleri.
6. Program/script: yayın klasöründeki `ResmiGazeteMailer.exe` dosyasının tam yolu
   (örn. `C:\Araclar\ResmiGazeteMailer\yayin\ResmiGazeteMailer.exe`).
7. "Başlangıç" (Start in) alanına aynı klasörü yazın — appsettings.json'ın
   bulunabilmesi için önemlidir.
8. Bitir. Görevi sağ tıklayıp "Çalıştır" ile hemen test edebilirsiniz.

### Komut satırından (schtasks):

```cmd
schtasks /create /tn "ResmiGazeteGunlukMail" /tr "C:\Araclar\ResmiGazeteMailer\yayin\ResmiGazeteMailer.exe" /sc daily /st 09:00
```

## 7. Linux/macOS alternatifi (cron)

```bash
dotnet publish -c Release -r linux-x64 --self-contained false -o ./yayin
crontab -e
# Her gün saat 09:00'da çalıştır:
0 9 * * * cd /path/to/yayin && dotnet ResmiGazeteMailer.dll >> /var/log/resmigazete-mail.log 2>&1
```

## 8. Genişletme fikirleri

- RSS'te sadece belirli bölümleri (örn. sadece "YÖNETMELİK") filtreleyin.
- Aynı gün için tekrar mail atılmasın diye gönderilen tarihleri bir metin
  dosyasına ("son_gonderim.txt") kaydedip kontrol edin.
- HTML gövdesini bir Razor şablonuna taşıyıp daha şık bir tasarım yapın.
- Birden fazla alıcı grubu için appsettings.json'a farklı listeler ekleyin.
