using AgentGatewayConsole.Tests.Helpers;

namespace AgentGatewayConsole.Tests.Services;

public class MessageServiceTests
{
    [Fact]
    public void Messages_TrAndEn_HaveSameKeys()
    {
        var service = TestMessageServiceFactory.Create();

        var tr = service.GetAllKeys("tr");
        var en = service.GetAllKeys("en");

        Assert.NotEmpty(tr);
        Assert.Empty(tr.Except(en));   // TR'de olup EN'de olmayan anahtar yok
        Assert.Empty(en.Except(tr));   // EN'de olup TR'de olmayan anahtar yok
    }

    [Fact]
    public void Get_ExistingKeyWithArgs_FormatsMessage()
    {
        var service = TestMessageServiceFactory.Create("tr");

        Assert.Equal("Girdi 10 · Çıktı 5", service.Get("Dashboard", "TokensSub", 10, 5));
    }

    [Fact]
    public void Get_MissingKey_ReturnsBracketedFallback()
    {
        var service = TestMessageServiceFactory.Create();

        Assert.Equal("[Yok.Anahtar]", service.Get("Yok", "Anahtar"));
    }
}
