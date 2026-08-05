using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class AdminBasvurulari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminBasvurular",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Gerekce = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Durum = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KararVerenUserId = table.Column<int>(type: "int", nullable: true),
                    KararTarihi = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RedNedeni = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminBasvurular", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminBasvurular_Durum_CreatedAt",
                table: "AdminBasvurular",
                columns: new[] { "Durum", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminBasvurular_UserId",
                table: "AdminBasvurular",
                column: "UserId",
                unique: true,
                filter: "[Durum] = 'beklemede'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminBasvurular");
        }
    }
}
