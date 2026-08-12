using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETicaretAPI.Migrations
{
    /// <inheritdoc />
    public partial class Sozlesmeler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sozlesmeler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tip = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Surum = table.Column<int>(type: "int", nullable: false),
                    Icerik = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    YayinTarihi = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AktifMi = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sozlesmeler", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SozlesmeOnaylari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SozlesmeId = table.Column<int>(type: "int", nullable: false),
                    OnayTarihi = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAdresi = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SozlesmeOnaylari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SozlesmeOnaylari_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SozlesmeOnaylari_Sozlesmeler_SozlesmeId",
                        column: x => x.SozlesmeId,
                        principalTable: "Sozlesmeler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SozlesmeOnaylari_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sozlesmeler_Tip_AktifMi",
                table: "Sozlesmeler",
                columns: new[] { "Tip", "AktifMi" },
                unique: true,
                filter: "[AktifMi] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SozlesmeOnaylari_OrderId",
                table: "SozlesmeOnaylari",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SozlesmeOnaylari_SozlesmeId",
                table: "SozlesmeOnaylari",
                column: "SozlesmeId");

            migrationBuilder.CreateIndex(
                name: "IX_SozlesmeOnaylari_UserId_OnayTarihi",
                table: "SozlesmeOnaylari",
                columns: new[] { "UserId", "OnayTarihi" });

            // ⭐ Baslangic sozlesme metinleri (surum 1).
            //
            // ⚠️ Metinler TASLAKTIR, hukuki inceleme gerekiyor — admin
            // panelindeki sozlesme ekrani bunu uyari olarak gosteriyor.
            // Metin degisirse YENI SURUM eklenir, bu satirlar
            // guncellenmez: eski onaylar eski metne bagli kalmali.
            migrationBuilder.Sql(@"
INSERT INTO Sozlesmeler (Tip, Surum, Icerik, YayinTarihi, AktifMi) VALUES
 ('gizlilik', 1, N'SATIK - GIZLILIK POLITIKASI

1. VERI SORUMLUSU
Satik (Magaza), bu uygulama uzerinden topladigi kisisel verilerin
veri sorumlusudur.

2. ISLENEN VERILER
- Kimlik ve iletisim: ad soyad, e-posta, telefon numarasi
- Teslimat: adres basligi, acik adres, sehir
- Siparis: satin alinan urunler, tutarlar, odeme durumu, kartin son
  4 hanesi
- Kullanim: oturum bilgisi, cihaz bilgisi, IP adresi

Kart numarasinin tamami ve CVV HICBIR ZAMAN saklanmaz.
Sifreler geri donusturulemez sekilde ozetlenerek saklanir.

3. ISLEME AMACLARI
Siparis olusturmak ve teslim etmek, odeme kaydi tutmak, destek
taleplerini yanitlamak, iade sureclerini yurutmek, yasal saklama
yukumluluklerini yerine getirmek.

4. SAKLAMA SURESI
Siparis ve odeme kayitlari, ilgili mali mevzuatin ongordugu sure
boyunca saklanir. Hesabinizi kapattiginizda kimlik ve iletisim
bilgileriniz anonimlestirilir; siparis kayitlari ticari kayit
olarak kalir.

5. HAKLARINIZ (KVKK md. 11)
Kisisel verilerinizin islenip islenmedigini ogrenme, duzeltme,
silme ve verilerinizin bir kopyasini talep etme hakkina sahipsiniz.
Uygulamadaki Verilerimi Indir ve Hesabimi Kapat ekranlari bu
haklari dogrudan kullanmanizi saglar.

6. UCUNCU TARAFLAR
Kargo firmalarina yalnizca teslimat icin gerekli bilgiler
(ad, adres, telefon) aktarilir.', GETUTCDATE(), 1),
 ('kullanim', 1, N'SATIK - KULLANIM KOSULLARI

1. TARAFLAR
Bu kosullar, Satik ile uygulamayi kullanan kullanici arasindadir.

2. HESAP
Hesap bilgilerinizin gizliligi sizin sorumlulugunuzdadir. Verdiginiz
bilgilerin dogru ve guncel olmasi gerekir. Bir hesabin baskasi adina
acilmasi yasaktir.

3. SIPARIS
Siparis, magaza tarafindan onaylandiginda kurulmus sayilir. Stok
tukenmesi ya da fiyat hatasi gibi durumlarda magaza siparisi iptal
edip odemeyi iade edebilir.

4. FIYATLAR
Uygulamada gosterilen fiyatlar KDV dahildir. Kargo ucreti siparis
ozetinde ayrica gosterilir.

5. YASAK KULLANIM
Sistemin isleyisini bozacak, guvenlik onlemlerini asmayi amaclayan
ya da baskalarinin verisine erismeye calisan kullanim yasaktir.

6. DEGISIKLIK
Bu kosullar guncellenebilir. Guncel surum her zaman uygulamada
yayinlanir; eski onaylar verildikleri surume bagli kalir.', GETUTCDATE(), 1),
 ('mesafeli_satis', 1, N'MESAFELI SATIS SOZLESMESI

1. TARAFLAR
SATICI: Satik
ALICI: Siparisi veren kullanici

2. KONU
Alici, siparis ozetinde belirtilen urunlerin satisi ve teslimi
konusunda asagidaki kosullari kabul eder.

3. ODEME VE TESLIMAT
Odeme, siparis aninda alinir. Teslimat, siparis ozetinde belirtilen
adrese kargo ile yapilir. Kargo ucreti siparis ozetinde gosterilir.

4. CAYMA HAKKI
Alici, malin teslim alindigi tarihten itibaren 14 GUN icinde hicbir
gerekce gostermeksizin ve cezai sart odemeksizin cayma hakkini
kullanabilir. Cayma bildirimi uygulamadaki Iade Talebi ekranindan
yapilir.

Cayma hakkinin kullanilmasi halinde urun bedeli ve standart teslimat
masrafi, urunun saticiya ulasmasindan sonra iade edilir.

5. CAYMA HAKKININ ISTISNALARI
Tuketicinin istekleri dogrultusunda kisisellestirilen, cabuk bozulan
ya da hijyen acisindan iadesi uygun olmayan urunlerde cayma hakki
kullanilamaz.

6. UYUSMAZLIK
Uyusmazliklarda Tuketici Hakem Heyetleri ve Tuketici Mahkemeleri
yetkilidir.', GETUTCDATE(), 1),
 ('on_bilgilendirme', 1, N'ON BILGILENDIRME FORMU

1. SATICI BILGILERI
Unvan: Satik
Iletisim bilgileri uygulamanin Destek bolumunde yer alir.

2. URUN VE FIYAT
Siparis ettiginiz urunlerin adi, adedi ve birim fiyati siparis
ozetinde gosterilir. Tum fiyatlar KDV dahildir.

3. ODEME
Odeme siparis aninda, secilen kart ile tahsil edilir. Kartin yalnizca
son 4 hanesi kayit altina alinir.

4. TESLIMAT
Teslimat, siparis sirasinda sectiginiz adrese yapilir. Kargo ucreti
siparis ozetinde ayrica belirtilir; belirlenen tutarin uzerindeki
siparislerde kargo ucretsizdir.

5. CAYMA HAKKI
Teslim tarihinden itibaren 14 gun icinde cayma hakkiniz vardir.
Ayrintilar Mesafeli Satis Sozlesmesinde yer alir.

6. SIKAYET
Talepleriniz icin uygulamadaki Destek bolumunu kullanabilirsiniz.', GETUTCDATE(), 1);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SozlesmeOnaylari");

            migrationBuilder.DropTable(
                name: "Sozlesmeler");
        }
    }
}
