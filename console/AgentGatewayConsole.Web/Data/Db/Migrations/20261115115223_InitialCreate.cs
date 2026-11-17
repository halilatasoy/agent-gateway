using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgentGatewayConsole.Web.Data.Db.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GatewayRequestLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Protocol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    ClientAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Gateway = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Route = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResponseModel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: true),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: true),
                    McpMethod = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    McpTarget = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ToolName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    GuardrailOutcome = table.Column<int>(type: "integer", nullable: false),
                    GuardrailPhase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    GuardrailsRaw = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AttributesJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayRequestLogs", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Id", "Description", "Key", "UpdatedAtUtc", "Value" },
                values: new object[,]
                {
                    { 1, "Kayıtların saklanacağı gün sayısı", "Retention:Days", new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Utc), "30" },
                    { 2, "Özet ekranının yenilenme aralığı (sn)", "Dashboard:RefreshSeconds", new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Utc), "15" },
                    { 3, "Yakalanan veriler listesinde gösterilecek en fazla kayıt", "ProtectedData:MaxRows", new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Utc), "1000" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRequestLogs_GuardrailOutcome_OccurredAtUtc",
                table: "GatewayRequestLogs",
                columns: new[] { "GuardrailOutcome", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRequestLogs_OccurredAtUtc",
                table: "GatewayRequestLogs",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRequestLogs_Protocol_OccurredAtUtc",
                table: "GatewayRequestLogs",
                columns: new[] { "Protocol", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "GatewayRequestLogs");
        }
    }
}
