# ============================================================
#  ADIM 2 — API'nin kalıbı (image)
#
#  İKİ AŞAMALI (multi-stage) BUILD.
#
#  ⚠️ NEDEN İKİ AŞAMA?
#
#  Derlemek için .NET SDK gerekiyor (~800 MB: derleyici, NuGet,
#  analiz araçları). Ama ÇALIŞTIRMAK için sadece runtime yeter
#  (~220 MB). Tek aşama yazsaydık SDK'nın tamamı sunucuya giderdi:
#  hem gereksiz yer hem de gereksiz saldırı yüzeyi — derleyici
#  bulunduran bir sunucuya giren saldırgan orada kod derleyebilir.
#
#  Aşağıda "derleme" aşamasında iş bitiyor, sonra sadece ÇIKTI
#  ikinci aşamaya kopyalanıyor. SDK son imajda yok.
# ============================================================


# ---------- AŞAMA 1: DERLE ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS derleme
WORKDIR /kaynak

# ⚠️ ÖNCE SADECE .csproj KOPYALANIYOR, KOD DEĞİL.
#
# Docker her satırı bir katman olarak önbelleğe alır ve bir katman
# değişince ondan SONRAKİ her şeyi yeniden çalıştırır.
#
# Kodu da beraber kopyalasaydık tek bir satır değiştirdiğinde
# restore da baştan çalışır, her derlemede NuGet'e gidilirdi.
# Bu sırayla paket listesi değişmedikçe restore önbellekten geliyor:
# ilk derleme ~2 dk, sonrakiler ~20 sn.
COPY ETicaretAPI/ETicaretAPI.csproj ETicaretAPI/
RUN dotnet restore ETicaretAPI/ETicaretAPI.csproj

# Şimdi kod gelsin.
COPY ETicaretAPI/ ETicaretAPI/

# --no-restore: yukarıda zaten yaptık, iki kez yapmasın.
RUN dotnet publish ETicaretAPI/ETicaretAPI.csproj \
    -c Release \
    -o /yayin \
    --no-restore


# ---------- AŞAMA 2: ÇALIŞTIR ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Yalnızca derleme çıktısı geliyor. Kaynak kod, SDK, NuGet
# önbelleği — hiçbiri son imajda yok.
COPY --from=derleme /yayin .

# ⚠️ KLASÖRLER ÖNCEDEN AÇILIYOR.
#
# Kod zaten Directory.CreateDirectory çağırıyor, yani teknik olarak
# şart değil. Ama volume buraya bağlanacak; yol imajda hiç yoksa
# Docker'ın oluşturduğu boş dizinin sahipliği sürprizli olabiliyor.
# Bir satırla belirsizliği kaldırıyoruz.
RUN mkdir -p /app/wwwroot/uploads/urunler /app/wwwroot/uploads/imports

# ⚠️ 8080, 5289 DEĞİL.
#
# Konteynerin İÇİNDEKİ port. Dışarıya hangi portla çıkacağı
# docker-compose.yml'de belirleniyor (5289:8080) — yani admin ve
# mobil .env dosyalarındaki 5289 adresi aynen çalışmaya devam ediyor.
#
# .NET 8'den beri konteyner imajlarının varsayılanı 8080; 80'den
# taşındı çünkü 1024 altı portlar root yetkisi istiyor.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ETicaretAPI.dll"]
