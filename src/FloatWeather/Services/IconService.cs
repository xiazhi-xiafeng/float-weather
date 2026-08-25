using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using FontFamily = System.Windows.Media.FontFamily;

namespace FloatWeather.Services;

/// <summary>
/// 和风官方图标（QWeather Icons 字体）。
/// 从 jsDelivr 拉取官方图标字体 + CSS，解析「天气码 → 字形」，本地缓存后离线可用。
/// </summary>
public sealed partial class IconService
{
    private const string Base = "https://cdn.jsdelivr.net/npm/qweather-icons@1.8.0/font/";
    private const string FontFamilyName = "qweather-icons";

    private static string Dir => Path.Combine(AppContext.BaseDirectory, "icons");
    private static string FontFile => Path.Combine(Dir, "qweather-icons.ttf");
    private static string CssFile => Path.Combine(Dir, "qweather-icons.css");

    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _glyphs = new(StringComparer.Ordinal);
    private FontFamily? _font;

    public IconService(IHttpClientFactory factory) => _http = factory.CreateClient("icons");

    /// <summary>是否已就绪（字体已加载并解析出码点表）</summary>
    public bool IsReady { get; private set; }

    [GeneratedRegex("\\.qi-([\\w-]+)::before\\s*\\{\\s*content\\s*:\\s*\"\\\\([0-9a-fA-F]+)\"")]
    private static partial Regex GlyphRegex();

    /// <summary>确保字体/CSS 已下载解析（幂等）。</summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        if (IsReady) return;
        try
        {
            Directory.CreateDirectory(Dir);

            if (!File.Exists(FontFile))
                await DownloadAsync(Base + "fonts/qweather-icons.ttf", FontFile, ct);

            if (!File.Exists(CssFile))
                await DownloadAsync(Base + "qweather-icons.css", CssFile, ct);

            var css = await File.ReadAllTextAsync(CssFile, ct);
            foreach (Match m in GlyphRegex().Matches(css))
            {
                _glyphs[m.Groups[1].Value] = ((char)Convert.ToInt32(m.Groups[2].Value, 16)).ToString();
            }

            _font = new FontFamily(new Uri(Dir + Path.DirectorySeparatorChar), "./#" + FontFamilyName);
            IsReady = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[IconService] 加载失败: " + ex);
            // 加载失败则回退 emoji，不阻断主流程
        }
    }

    /// <summary>取天气码对应字形字符；未命中返回 null。</summary>
    public string? Glyph(string iconCode) =>
        !string.IsNullOrEmpty(iconCode) && _glyphs.TryGetValue(iconCode, out var g) ? g : null;

    /// <summary>图标字体（就绪后非空）。</summary>
    public FontFamily? Font => _font;

    private async Task DownloadAsync(string url, string dest, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        // 先写临时文件，完全关闭后再改名，避免占用冲突
        using (var tmp = new FileStream(dest + ".tmp", FileMode.Create, FileAccess.Write))
        {
            await src.CopyToAsync(tmp, ct);
        }
        File.Move(dest + ".tmp", dest, overwrite: true);
    }
}