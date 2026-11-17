using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AgentGatewayConsole.Web.Data.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetectedDataTypes",
                table: "GatewayRequestLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GatewayRequestContents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GatewayRequestLogId = table.Column<long>(type: "bigint", nullable: false),
                    DetailJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GatewayRequestContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GatewayRequestContents_GatewayRequestLogs_GatewayRequestLog~",
                        column: x => x.GatewayRequestLogId,
                        principalTable: "GatewayRequestLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AppSettings",
                columns: new[] { "Id", "Description", "Key", "UpdatedAtUtc", "Value" },
                values: new object[,]
                {
                    { 4, "Yakalanan içerikte saklanacak en fazla mesaj (son mesajlar ve verinin bulunduğu mesajlar öncelikli)", "Capture:MaxMessages", new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Utc), "30" },
                    { 5, "Yakalanan içerikte mesaj başına saklanacak en fazla karakter (maskelemeden sonra kısaltılır)", "Capture:MaxMessageChars", new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Utc), "2000" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_GatewayRequestContents_GatewayRequestLogId",
                table: "GatewayRequestContents",
                column: "GatewayRequestLogId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GatewayRequestContents");

            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DropColumn(
                name: "DetectedDataTypes",
                table: "GatewayRequestLogs");
        }
    }
}
