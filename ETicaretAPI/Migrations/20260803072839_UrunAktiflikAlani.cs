using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class UrunAktiflikAlani : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: true);       // ⭐ elle düzeltildi — aşağıdaki açıklamaya bak
        }

        // ⭐ ELLE DÜZELTİLDİ: EF'in ürettiği hali defaultValue: false idi.
        //
        // Sebep: EF, C# tarafındaki "= true" başlangıç değerini OKUMAZ.
        // Sadece tipin CLR varsayılanına bakar ve bool için o false'tur.
        // Dokunmasaydık migration çalıştığı anda veritabanındaki TÜM
        // mevcut ürünler IsActive = 0 olacak, yani mağaza tamamen
        // boşalacaktı.
        //
        // true yazmak iki işi birden yapıyor:
        //   1) Mevcut satırlar dolduruluyor (backfill)
        //   2) Kolona kalıcı bir DEFAULT 1 kısıtı kuruluyor — EF dışından
        //      INSERT edilen satırlar da satışta başlar

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Products");
        }
    }
}
