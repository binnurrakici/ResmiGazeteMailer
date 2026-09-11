using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

Console.OutputEncoding = Encoding.UTF8;

var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
if (!File.Exists(configPath))
{
    Console.WriteLine($"appsettings.json bulunamadı: {configPath}");
    return;
}

AppConfig? config;
try
{
    var configJson = File.ReadAllText(configPath);
    config = JsonSerializer.Deserialize<AppConfig>(configJson,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
}
catch (Exception ex)
{
    Console.WriteLine($"appsettings.json okunamadı: {ex.Message}");
    return;
}

if (config == null)
{
    Console.WriteLine("Konfigürasyon boş geldi.");
    return;
}

try
{
    Console.WriteLine("Günün Resmî Gazete içeriği alınıyor...");
    var sections = await FetchTodaySectionsAsync(config.Source.Url);

    if (sections.Count == 0)
    {
        Console.WriteLine("Bugüne ait öğe bulunamadı. E-posta gönderilmedi.");
        return;
    }

    Console.WriteLine("E-posta şablonu oluşturuluyor...");
    var htmlBody = GenerateHtmlEmail(DateTime.Now.ToString("dd MMMM yyyy, dddd"), sections);

    Console.WriteLine("E-posta gönderiliyor...");
    await SendEmailAsync(config.Smtp, htmlBody);
    Console.WriteLine("E-posta başarıyla gönderildi!");
}
catch (Exception ex)
{
    Console.WriteLine($"Hata oluştu: {ex}");
}

// --- Yardımcı Fonksiyonlar ---

static string GenerateHtmlEmail(string tarih, List<Section> sections)
{
    var sb = new StringBuilder();

    sb.Append($@"
    <!DOCTYPE html>
    <html lang=""tr"">
    <head>
      <meta charset=""UTF-8"">
      <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    </head>
    <body style=""margin: 0; padding: 0; background-color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; -webkit-font-smoothing: antialiased;"">
      <table width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""background-color: #f1f5f9; padding: 32px 12px;"">
        <tr>
          <td align=""center"">
            <!-- Ana Kart -->
            <table width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""max-width: 640px; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 16px rgba(15, 23, 42, 0.08);"">
              
              <!-- Başlık Bölümü -->
              <tr>
                <td style=""background: linear-gradient(135deg, #bf2b1b 0%, #ffffff 100%); padding: 36px 32px; text-align: left;"">
                  <span style=""display: inline-block; font-size: 11px; font-weight: 700; letter-spacing: 1.5px; text-transform: uppercase; color: #ea8a8a; margin-bottom: 8px;"">Günlük Bülten</span>
                  <h1 style=""margin: 0; color: #000000; font-size: 24px; font-weight: 700; letter-spacing: -0.5px;"">T.C. Resmî Gazete</h1>
                  <p style=""margin: 8px 0 0; color: #fbfdffc2; font-size: 14px;"">{tarih}</p>
                </td>
              </tr>

              <!-- Liste İçeriği -->
              <tr>
                <td style=""padding: 28px 32px;"">
    ");

    foreach (var section in sections)
    {
        sb.Append($@"
                  <div style=""margin-top: 24px; margin-bottom: 12px; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">
                    <h2 style=""margin: 0; font-size: 16px; font-weight: 700; color: #0f172a; text-transform: uppercase; letter-spacing: 0.5px;"">
                      {System.Net.WebUtility.HtmlEncode(section.Title)}
                    </h2>
                  </div>
        ");

        string? lastSubtitle = null;

        foreach (var item in section.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.Subtitle) && item.Subtitle != lastSubtitle)
            {
                //YÖNETMELİK BAŞLIĞI
                sb.Append($@"
                  <div style=""margin: 14px 0 8px 0;"">
                    <span style=""display: inline-block; background-color: #ea8a8a; color: #a72828; font-size: 11px; font-weight: 700; padding: 3px 8px; border-radius: 4px;"">
                      {System.Net.WebUtility.HtmlEncode(item.Subtitle)}
                    </span>
                  </div>
                ");
                lastSubtitle = item.Subtitle;
            }

            sb.Append($@"
                  <div style=""margin-bottom: 10px; padding: 12px 14px; background-color: #f8fafc; border-radius: 6px; border-left: 3px solid #ea8a8a;"">
                    <a href=""{item.Url}"" style=""display: block; font-size: 13.5px; font-weight: 500; color: #1e293b; text-decoration: none; line-height: 1.5;"">
                      {System.Net.WebUtility.HtmlEncode(item.Text)}
                    </a>
                  </div>
            ");
        }
    }

    sb.Append($@"
                </td>
              </tr>

              <!-- Alt Bilgi -->
              <tr>
                <td style=""background-color: #f8fafc; padding: 20px 32px; text-align: center; border-top: 1px solid #e2e8f0;"">
                  <p style=""margin: 0; font-size: 12px; color: #64748b; line-height: 1.6;"">
                    Bu e-posta otomatik Resmî Gazete bülten servisi tarafından oluşturulmuştur.<br>
                    <a href=""https://www.resmigazete.gov.tr"" style=""color: #0284c7; text-decoration: none; font-weight: 600;"">resmigazete.gov.tr</a>
                  </p>
                </td>
              </tr>

            </table>
          </td>
        </tr>
      </table>
    </body>
    </html>
    ");

    return sb.ToString();
}

static async Task<List<Section>> FetchTodaySectionsAsync(string pageUrl)
{
    using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

    await using var browser = await playwright.Chromium.LaunchAsync(new Microsoft.Playwright.BrowserTypeLaunchOptions
    {
        Headless = true
    });

    var page = await browser.NewPageAsync();

    await page.GotoAsync(pageUrl, new Microsoft.Playwright.PageGotoOptions
    {
        Timeout = 30000,
        WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle
    });

    var html = await page.ContentAsync();

    var doc = new HtmlDocument();
    doc.LoadHtml(html);

    var container = doc.DocumentNode.SelectSingleNode("//div[@id='html-content']");
    if (container == null)
    {
        Console.WriteLine("Beklenen '#html-content' bölümü sayfada bulunamadı. Site yapısı değişmiş olabilir.");
        return new List<Section>();
    }

    var sections = new List<Section>();
    Section? currentSection = null;
    string? currentSubtitle = null;

    foreach (var node in container.ChildNodes)
    {
        if (node.NodeType != HtmlNodeType.Element)
        {
            continue;
        }

        var classAttr = node.GetAttributeValue("class", "");

        if (classAttr.Contains("html-title"))
        {
            currentSection = new Section { Title = CleanText(node.InnerText) };
            sections.Add(currentSection);
            currentSubtitle = null;
        }
        else if (classAttr.Contains("html-subtitle"))
        {
            currentSubtitle = CleanText(node.InnerText);
        }
        else if (classAttr.Contains("fihrist-item"))
        {
            var link = node.SelectSingleNode(".//a");
            if (link == null)
            {
                continue;
            }

            var href = link.GetAttributeValue("href", "");
            if (!string.IsNullOrWhiteSpace(href) && !href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                href = new Uri(new Uri("https://www.resmigazete.gov.tr"), href).ToString();
            }

            var text = CleanText(link.InnerText);

            if (currentSection == null)
            {
                currentSection = new Section { Title = "Resmî Gazete" };
                sections.Add(currentSection);
            }

            currentSection.Items.Add(new Item
            {
                Subtitle = currentSubtitle,
                Text = text,
                Url = href
            });
        }
    }

    return sections;
}

static string CleanText(string raw)
{
    var decoded = System.Net.WebUtility.HtmlDecode(raw);
    return decoded.Trim();
}

static async Task SendEmailAsync(SmtpConfig smtp, string htmlBody)
{
    var message = new MimeMessage();
    message.From.Add(new MailboxAddress(smtp.FromName, smtp.From));

    foreach (var to in smtp.To)
    {
        message.To.Add(MailboxAddress.Parse(to));
    }

    message.Subject = $"{DateTime.Now:dd.MM.yyyy} Resmî Gazete Günlük Bülten";
    message.Body = new TextPart("html") { Text = htmlBody };

    using var client = new SmtpClient();

    client.CheckCertificateRevocation = false;

    SecureSocketOptions secureOption = smtp.UseSsl 
        ? SecureSocketOptions.StartTls 
        : SecureSocketOptions.Auto;

    await client.ConnectAsync(smtp.Host, smtp.Port, secureOption);
    await client.AuthenticateAsync(smtp.User, smtp.Password);
    await client.SendAsync(message);
    await client.DisconnectAsync(true);
}

// --- Modeller ---

class Section
{
    public string Title { get; set; } = "";
    public List<Item> Items { get; set; } = new();
}

class Item
{
    public string? Subtitle { get; set; }
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
}

class AppConfig
{
    public SourceConfig Source { get; set; } = new();
    public SmtpConfig Smtp { get; set; } = new();
}

class SourceConfig
{
    public string Url { get; set; } = "";
}

class SmtpConfig
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "";
    public List<string> To { get; set; } = new();
}