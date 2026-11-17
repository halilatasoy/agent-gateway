using AgentGatewayConsole.Tests.Helpers;
using AgentGatewayConsole.Web.Models;
using AgentGatewayConsole.Web.Services;

namespace AgentGatewayConsole.Tests.Services;

public class SensitiveDataRedactorTests
{
    [Theory]
    [InlineData("Musteri TC 12345678901 kaydi", "TcKimlik", "<TC_KIMLIK_NO>", "12345678901")]
    [InlineData("IBAN TR33 0006 1005 1978 6457 8413 26 hesabina", "Iban", "<IBAN>", "0006 1005")]
    [InlineData("Kart 4111 1111 1111 1111 ile odendi", "CreditCard", "<KART_NO>", "4111 1111")]
    [InlineData("Ara: 0532 123 45 67", "Phone", "<TELEFON>", "123 45 67")]
    [InlineData("Ara: +90 532 123 45 67", "Phone", "<TELEFON>", "532 123")]
    [InlineData("Yaz: deneme.kisi@ornek.com.tr adresine", "Email", "<E_POSTA>", "deneme.kisi@")]
    [InlineData("Referans 987654321098 numarali", SensitiveDataRedactor.NumberType, "<SAYI>", "987654321098")]
    public void Redact_SensitiveValue_ReplacesWithPlaceholderAndReportsType(string input, string type, string placeholder, string rawPart)
    {
        var result = TestContent.Redactor().Redact(input);

        Assert.Contains(placeholder, result.Text);
        Assert.DoesNotContain(rawPart, result.Text);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(type, hit.Type);
        Assert.Equal(1, hit.Count);
    }

    [Fact]
    public void Redact_CleanText_ReturnsUnchangedWithoutHits()
    {
        // Sürüm numarası ve tarih (8 hane) güvenlik ağına takılmamalı.
        const string input = "Merhaba, surum 1.0.105 ve tarih 2026-11-15 icin ozet hazirla.";

        var result = TestContent.Redactor().Redact(input);

        Assert.Equal(input, result.Text);
        Assert.Empty(result.Hits);
    }

    [Fact]
    public void Redact_MultipleValues_CountsEachAndSnippetHasNoRawValue()
    {
        const string input = "Birinci TC 12345678901, ikinci TC 10987654321; iletisim a@b.com";

        var result = TestContent.Redactor().Redact(input);

        var tc = Assert.Single(result.Hits, h => h.Type == "TcKimlik");
        Assert.Equal(2, tc.Count);
        Assert.Contains("<TC_KIMLIK_NO>", tc.Snippet);
        Assert.DoesNotContain("12345678901", tc.Snippet);
        Assert.Contains(result.Hits, h => h.Type == "Email");
    }

    [Fact]
    public void Redact_EmptyRuleConfig_FallsBackToDefaultRules()
    {
        var redactor = TestContent.Redactor(new RedactionOptions { Rules = new() });

        var result = redactor.Redact("TC 12345678901");

        Assert.Equal("TC <TC_KIMLIK_NO>", result.Text);
    }

    [Fact]
    public void Redact_CustomRuleOnly_SafetyNetStillMasksLongNumbers()
    {
        var options = new RedactionOptions
        {
            Rules = new() { new RedactionRule { Type = "Plate", Pattern = @"\b34 ?[A-Z]{1,3} ?\d{2,4}\b", Placeholder = "<PLAKA>" } }
        };

        var result = TestContent.Redactor(options).Redact("Arac 34 ABC 123, TC 12345678901");

        Assert.Contains("<PLAKA>", result.Text);
        Assert.Contains("<SAYI>", result.Text);
        Assert.DoesNotContain("12345678901", result.Text);
    }

    [Fact]
    public void Redact_NullOrEmpty_ReturnsEmptyWithoutHits()
    {
        var redactor = TestContent.Redactor();

        Assert.Empty(redactor.Redact(null).Hits);
        Assert.Equal(string.Empty, redactor.Redact(string.Empty).Text);
    }
}
