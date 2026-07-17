using Hangfire.Dashboard;

namespace ETicaretAPI.Support
{
    // Hangfire dashboard'una kimin erişebileceğini belirler.
    // Şimdilik: sadece sunucunun kendisinden (localhost) açılabilsin.
    // Canlıya çıkarsan burayı gerçek admin kontrolüyle sıkılaştırırsın.
    public class HangfireYetkiFiltresi : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            var ip = httpContext.Connection.RemoteIpAddress;

            return ip != null && System.Net.IPAddress.IsLoopback(ip);
        }
    }
}
