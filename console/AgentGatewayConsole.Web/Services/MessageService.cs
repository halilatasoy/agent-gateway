using System.Text.Json;
using AgentGatewayConsole.Web.Models;
using Microsoft.Extensions.Options;

namespace AgentGatewayConsole.Web.Services;

/// <summary>
/// JSON tabanlı mesaj (i18n) servisi — webds MessageService'in sade hâli.
/// Dil önceliği: cookie (AgwConsole.Language) → appsettings Settings:Language.
/// </summary>
public class MessageService
{
    public const string LanguageCookie = "AgwConsole.Language";
    public static readonly string[] SupportedLanguages = { "tr", "en" };

    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _messages = new();
    private readonly string _defaultLanguage;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MessageService> _logger;

    public MessageService(IOptions<ConsoleSettings> settings, IWebHostEnvironment environment,
        IHttpContextAccessor httpContextAccessor, ILogger<MessageService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _defaultLanguage = SupportedLanguages.Contains(settings.Value.Language) ? settings.Value.Language : "tr";

        var resourcePath = Path.Combine(environment.ContentRootPath, "Resources");
        foreach (var lang in SupportedLanguages)
            LoadLanguageFile(resourcePath, lang);
    }

    private void LoadLanguageFile(string resourcePath, string language)
    {
        var filePath = Path.Combine(resourcePath, $"Messages.{language}.json");
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Mesaj dosyası bulunamadı: {Path}", filePath);
            return;
        }
        var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
        if (parsed is not null)
            _messages[language] = parsed;
    }

    public string CurrentLanguage
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context is not null
                && context.Request.Cookies.TryGetValue(LanguageCookie, out var lang)
                && !string.IsNullOrEmpty(lang)
                && _messages.ContainsKey(lang))
                return lang;
            return _defaultLanguage;
        }
    }

    public string Get(string category, string key, params object[] args)
    {
        var lang = _messages.ContainsKey(CurrentLanguage) ? CurrentLanguage : _defaultLanguage;
        if (TryGet(lang, category, key, out var message) || TryGet(_defaultLanguage, category, key, out message))
            return args.Length > 0 ? string.Format(message, args) : message;
        return $"[{category}.{key}]";
    }

    public string GetError(string key, params object[] args) => Get("Errors", key, args);

    /// <summary>İstenen kategorilerin tüm anahtarlarını döner — /api/messages için.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetCategories(IEnumerable<string> categories)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var cat in categories.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            result[cat] = GetCategory(CurrentLanguage, cat)
                ?? GetCategory(_defaultLanguage, cat)
                ?? new Dictionary<string, string>();
        }
        return result;
    }

    /// <summary>Bir dildeki tüm "Kategori:Anahtar" çiftleri — parity testi için.</summary>
    public IReadOnlySet<string> GetAllKeys(string language)
        => _messages.TryGetValue(language, out var cats)
            ? cats.SelectMany(c => c.Value.Keys.Select(k => $"{c.Key}:{k}")).ToHashSet()
            : new HashSet<string>();

    private bool TryGet(string language, string category, string key, out string message)
    {
        message = string.Empty;
        var cat = GetCategory(language, category);
        if (cat is null || !cat.TryGetValue(key, out var value))
            return false;
        message = value;
        return true;
    }

    private Dictionary<string, string>? GetCategory(string language, string category)
        => _messages.TryGetValue(language, out var cats) && cats.TryGetValue(category, out var cat) ? cat : null;
}
