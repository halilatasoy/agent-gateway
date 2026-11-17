using AgentGatewayConsole.Web.Data.Db.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentGatewayConsole.Web.Data.Db;

public class ConsoleDbContext : DbContext
{
    public ConsoleDbContext(DbContextOptions<ConsoleDbContext> options) : base(options) { }

    public DbSet<GatewayRequestLog> GatewayRequestLogs => Set<GatewayRequestLog>();
    public DbSet<GatewayRequestContent> GatewayRequestContents => Set<GatewayRequestContent>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GatewayRequestLog>(e =>
        {
            e.ToTable("GatewayRequestLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Protocol).HasMaxLength(16).IsRequired();
            e.Property(x => x.HttpMethod).HasMaxLength(16);
            e.Property(x => x.Path).HasMaxLength(1024);
            e.Property(x => x.ClientAddress).HasMaxLength(64);
            e.Property(x => x.Gateway).HasMaxLength(256);
            e.Property(x => x.Route).HasMaxLength(256);
            e.Property(x => x.Provider).HasMaxLength(64);
            e.Property(x => x.RequestModel).HasMaxLength(256);
            e.Property(x => x.ResponseModel).HasMaxLength(256);
            e.Property(x => x.McpMethod).HasMaxLength(128);
            e.Property(x => x.McpTarget).HasMaxLength(256);
            e.Property(x => x.ToolName).HasMaxLength(256);
            e.Property(x => x.GuardrailPhase).HasMaxLength(16);
            e.Property(x => x.GuardrailsRaw).HasMaxLength(4000);
            e.Property(x => x.Reason).HasMaxLength(128);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.Property(x => x.DetectedDataTypes).HasMaxLength(256);

            // Npgsql: jsonb; diğer sağlayıcılarda (InMemory testleri) düz metin.
            if (Database.IsNpgsql())
                e.Property(x => x.AttributesJson).HasColumnType("jsonb");

            e.HasIndex(x => x.OccurredAtUtc);
            e.HasIndex(x => new { x.GuardrailOutcome, x.OccurredAtUtc });
            e.HasIndex(x => new { x.Protocol, x.OccurredAtUtc });

            // 1:1 içerik; log silinince içerik de veritabanında cascade ile silinir.
            e.HasOne(x => x.Content)
                .WithOne(x => x.GatewayRequestLog)
                .HasForeignKey<GatewayRequestContent>(x => x.GatewayRequestLogId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GatewayRequestContent>(e =>
        {
            e.ToTable("GatewayRequestContents");
            e.HasKey(x => x.Id);
            e.Property(x => x.DetailJson).IsRequired();
            e.HasIndex(x => x.GatewayRequestLogId).IsUnique();
            if (Database.IsNpgsql())
                e.Property(x => x.DetailJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("AppSettings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(128).IsRequired();
            e.Property(x => x.Value).HasMaxLength(1024).IsRequired();
            e.Property(x => x.Description).HasMaxLength(512);
            e.HasIndex(x => x.Key).IsUnique();

            // Varsayılan çalışma zamanı ayarları
            var seededAt = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
            e.HasData(
                new AppSetting { Id = 1, Key = SettingKeys.RetentionDays, Value = "30", Description = "Kayıtların saklanacağı gün sayısı", UpdatedAtUtc = seededAt },
                new AppSetting { Id = 2, Key = SettingKeys.DashboardRefreshSeconds, Value = "15", Description = "Özet ekranının yenilenme aralığı (sn)", UpdatedAtUtc = seededAt },
                new AppSetting { Id = 3, Key = SettingKeys.ProtectedEventsMaxRows, Value = "1000", Description = "Yakalanan veriler listesinde gösterilecek en fazla kayıt", UpdatedAtUtc = seededAt },
                new AppSetting { Id = 4, Key = SettingKeys.CaptureMaxMessages, Value = "30", Description = "Yakalanan içerikte saklanacak en fazla mesaj (son mesajlar ve verinin bulunduğu mesajlar öncelikli)", UpdatedAtUtc = seededAt },
                new AppSetting { Id = 5, Key = SettingKeys.CaptureMaxMessageChars, Value = "2000", Description = "Yakalanan içerikte mesaj başına saklanacak en fazla karakter (maskelemeden sonra kısaltılır)", UpdatedAtUtc = seededAt });
        });
    }
}

/// <summary>AppSettings tablosundaki anahtarlar.</summary>
public static class SettingKeys
{
    public const string RetentionDays = "Retention:Days";
    public const string DashboardRefreshSeconds = "Dashboard:RefreshSeconds";
    public const string ProtectedEventsMaxRows = "ProtectedData:MaxRows";
    public const string CaptureMaxMessages = "Capture:MaxMessages";
    public const string CaptureMaxMessageChars = "Capture:MaxMessageChars";
}
