using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentGatewayConsole.Web.Data.Db;

/// <summary>
/// `dotnet ef migrations` için tasarım zamanı fabrikası. Gerçek bağlantı kurulmaz;
/// connection string yalnızca Npgsql sağlayıcısını seçmek için verilir.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ConsoleDbContext>
{
    public ConsoleDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ConsoleDbContext>()
            .UseNpgsql("Host=localhost;Database=agwconsole;Username=design;Password=design")
            .Options;
        return new ConsoleDbContext(options);
    }
}
