using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class KampanyalarEklendi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Kampanyalar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Baslik = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    KisaAciklama = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BitisMetni = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Aciklama = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    GorselUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    KuponKodlari = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Kosullar = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Sira = table.Column<int>(type: "int", nullable: false),
                    AktifMi = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kampanyalar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Kampanyalar_AktifMi_Sira",
                table: "Kampanyalar",
                columns: new[] { "AktifMi", "Sira" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Kampanyalar");
        }
    }
}
