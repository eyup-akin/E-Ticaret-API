namespace ETicaretAPI.Services
{
    // GELİŞTİRME göndericisi: maili GERÇEKTEN göndermez, konsola/log'a basar.
    // Böylece domain/servis olmadan akışı uçtan uca test edebiliriz.
    // Canlıda bunun yerine gerçek (SMTP/API) bir gönderici tanıtılır — interface
    // aynı olduğu için controller'lar bundan hiç etkilenmez.
    public class KonsolEmailGonderici : IEmailGonderici
    {
        private readonly ILogger<KonsolEmailGonderici> _log;

        public KonsolEmailGonderici(ILogger<KonsolEmailGonderici> log)
        {
            _log = log;
        }

        public Task GonderAsync(string aliciEmail, string konu, string govdeHtml)
        {
            // LogWarning kullandık ki konsolda göze çarpsın (Information'da kaybolmasın).
            _log.LogWarning(
                "\n===== [DEV EMAIL] =====\nKime: {Alici}\nKonu: {Konu}\n{Govde}\n=======================",
                aliciEmail, konu, govdeHtml);

            return Task.CompletedTask; // gerçek gönderim yok, işi bitti say
        }
    }
}