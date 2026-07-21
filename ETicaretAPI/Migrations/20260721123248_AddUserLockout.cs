using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "KilitBitis",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YanlisGirisSayisi",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KilitBitis",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "YanlisGirisSayisi",
                table: "Users");
        }
    }
}
