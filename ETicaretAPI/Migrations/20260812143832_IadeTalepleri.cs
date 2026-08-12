using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class IadeTalepleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReturnRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderItemId = table.Column<int>(type: "int", nullable: true),
                    Sebep = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Aciklama = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Durum = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    TalepTarihi = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KararTarihi = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KararVerenUserId = table.Column<int>(type: "int", nullable: true),
                    RedNedeni = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IadeTutari = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReturnRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_Durum_TalepTarihi",
                table: "ReturnRequests",
                columns: new[] { "Durum", "TalepTarihi" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_OrderId_OrderItemId",
                table: "ReturnRequests",
                columns: new[] { "OrderId", "OrderItemId" },
                unique: true,
                filter: "[Durum] <> 'reddedildi' AND [Durum] <> 'para_iade_edildi'");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnRequests_OrderItemId",
                table: "ReturnRequests",
                column: "OrderItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnRequests");
        }
    }
}
