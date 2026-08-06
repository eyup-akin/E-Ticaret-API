using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class KdvOranlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ⭐ ELLE DÜZELTİLDİ — defaultValue 0 DEĞİL 20.
            //
            // EF bunu 0 olarak üretti çünkü modeldeki "= 20" bir C#
            // alan başlatıcısıdır ve veritabanına yansımaz. Aynı tuzağı
            // Product.IsActive'de de yaşamıştık; oradaki yorum zaten
            // "eski satırların doldurulması migration'da ayrıca
            // halledilecek" diyor.
            //
            // 0 ile bıraksaydık mevcut TÜM ürünler %0 KDV'li görünürdü
            // ve ekranda "KDV (%0): 0,00 TL" yazardı — eksik değil,
            // YANLIŞ bilgi.
            //
            // 20 yazmak uydurma değil: bugüne kadar sistemde KDV hiç
            // yoktu, ürünlerin hepsi fiilen genel orana tabiydi.
            //
            // defaultValue ayrıca kolona bir DEFAULT kısıtı koyar;
            // ileride oran belirtmeden eklenen satırlar da 20 alır.
            migrationBuilder.AddColumn<int>(
                name: "VatRate",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 20);

            // ⚠️ Aşağıdaki İKİ kolon BİLEREK doldurulmuyor.
            //
            // Bunlar geçmiş hakkında iddia taşıyor: o siparişlerde hangi
            // KDV oranının uygulandığını bilmiyoruz — sistemde KDV kaydı
            // yoktu. 0 yazmak "KDV'siz satış yapıldı" demek olurdu.
            //
            // Null kalınca ekranlar KDV satırını hiç çizmiyor.
            // Modellerdeki yorumlarda gerekçe uzun uzun yazılı.
            migrationBuilder.AddColumn<int>(
                name: "ShippingVatRate",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VatRate",
                table: "OrderItems",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShippingVatRate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "OrderItems");
        }
    }
}
