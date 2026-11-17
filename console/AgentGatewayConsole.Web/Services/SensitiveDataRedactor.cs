using System.Text.RegularExpressions;
using AgentGatewayConsole.Web.Models;
using Microsoft.Extensions.Options;

namespace AgentGatewayConsole.Web.Services;

/// <summary>Maskelenmiş metin ve bulunan veri türleri.</summary>
public sealed record RedactionResult(string Text, IReadOnlyList<RedactionHit> Hits);

/// <summary>Bir veri türünün eşleşme sayısı ve ilk eşleşmenin çevresindeki maskelenmiş metin.</summary>
public sealed record RedactionHit(string Type, int Count, string Snippet);

/// <summary>
/// Panelin kendi maskeleme katmanı. Gateway'in log'a eklediği istek gövdesi ham (maskesiz) gelir;
/// bu sınıf onu kaydetmeden önce maskeler. Kurallar sırayla uygulanır (IBAN kart deseninden, kart TC
/// deseninden önce gelmeli). En sona her zaman bir güvenlik ağı eklenir: 9+ haneli her sayı maskelenir,
/// böylece gateway'in yerleşik kuralları (ör. PhoneNumber) panelin desenlerinden farklı olsa bile
/// ham sayı veritabanına girmez.
/// </summary>
public class SensitiveDataRedactor
{
    public const string NumberType = "Number";
    private const int SnippetRadius = 60;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    public static readonly IReadOnlyList<RedactionRule> DefaultRules = new List<RedactionRule>
    {
        new() { Type = "Iban", Pattern = @"(?i)\bTR\d{2}(?:[ ]?\d{4}){5}[ ]?\d{2}\b", Placeholder = "<IBAN>" },
        new() { Type = "CreditCard", Pattern = @"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)", Placeholder = "<KART_NO>" },
        new() { Type = "TcKimlik", Pattern = @"(?<!\d)[1-9]\d{10}(?!\d)", Placeholder = "<TC_KIMLIK_NO>" },
        new() { Type = "Phone", Pattern = @"(?<![\d+])(?:\+90[ -]?|0)?5\d{2}[ -]?\d{3}[ -]?\d{2}[ -]?\d{2}(?!\d)", Placeholder = "<TELEFON>" },
        new() { Type = "Email", Pattern = @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", Placeholder = "<E_POSTA>" }
    };

    // Güvenlik ağı: boşluk, tire veya parantezle ayrılmış olabilen 9+ haneli sayılar.
    private static readonly RedactionRule SafetyNetRule = new()
    {
        Type = NumberType, Pattern = @"(?<!\d)\d(?:[ ()-]?\d){8,}(?!\d)", Placeholder = "<SAYI>"
    };

    private readonly IReadOnlyList<(RedactionRule Rule, Regex Regex)> _rules;

    public SensitiveDataRedactor(IOptions<RedactionOptions> options)
    {
        var configured = options.Value.Rules
            .Where(r => !string.IsNullOrWhiteSpace(r.Type) && !string.IsNullOrWhiteSpace(r.Pattern) && !string.IsNullOrWhiteSpace(r.Placeholder))
            .ToList();
        var rules = (configured.Count > 0 ? configured : DefaultRules).Append(SafetyNetRule);
        _rules = rules
            .Select(r => (r, new Regex(r.Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, MatchTimeout)))
            .ToList();
    }

    /// <summary>Kuralların uygulanma sırası; bulguları önem sırasıyla dizmek için kullanılır.</summary>
    public IReadOnlyList<string> TypeOrder => _rules.Select(r => r.Rule.Type).ToList();

    /// <summary>
    /// Metni maskeler. Desen zaman aşımına uğrarsa <see cref="RegexMatchTimeoutException"/> fırlatır;
    /// çağıran taraf bu durumda içeriği hiç saklamamalıdır.
    /// </summary>
    public RedactionResult Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return new RedactionResult(string.Empty, Array.Empty<RedactionHit>());

        var current = text;
        var counts = new List<(RedactionRule Rule, int Count)>();
        foreach (var (rule, regex) in _rules)
        {
            var n = 0;
            current = regex.Replace(current, _ =>
            {
                n++;
                return rule.Placeholder;
            });
            if (n > 0)
                counts.Add((rule, n));
        }

        var hits = counts
            .Select(c => new RedactionHit(c.Rule.Type, c.Count, Snippet(current, c.Rule.Placeholder)))
            .ToList();
        return new RedactionResult(current, hits);
    }

    /// <summary>Maskelenmiş metinde etiketin ilk geçtiği yerin çevresi (tek satıra indirilmiş).</summary>
    private static string Snippet(string redacted, string placeholder)
    {
        var idx = redacted.IndexOf(placeholder, StringComparison.Ordinal);
        if (idx < 0)
            return string.Empty;
        var start = Math.Max(0, idx - SnippetRadius);
        var end = Math.Min(redacted.Length, idx + placeholder.Length + SnippetRadius);
        var part = Regex.Replace(redacted[start..end], @"\s+", " ").Trim();
        return (start > 0 ? "…" : string.Empty) + part + (end < redacted.Length ? "…" : string.Empty);
    }
}
