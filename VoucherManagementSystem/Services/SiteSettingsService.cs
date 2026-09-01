using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VoucherManagementSystem.Data;

namespace VoucherManagementSystem.Services
{
    // Serves the admin-configured site name to the views. Every page renders the name,
    // so the value is cached in memory and refreshed only when Theme Settings are saved.
    public interface ISiteSettingsService
    {
        Task<string> GetSiteNameAsync();
        Task<string> GetSiteShortNameAsync();
        void Invalidate();
    }

    public class SiteSettingsService : ISiteSettingsService
    {
        public const string DefaultSiteName = "AL-Hafiz Voucher System";
        public const string DefaultSiteShortName = "AL-Hafiz";

        private const string CacheKey = "SiteBranding";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public SiteSettingsService(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<string> GetSiteNameAsync() => (await GetBrandingAsync()).Name;

        public async Task<string> GetSiteShortNameAsync() => (await GetBrandingAsync()).ShortName;

        public void Invalidate() => _cache.Remove(CacheKey);

        private async Task<(string Name, string ShortName)> GetBrandingAsync()
        {
            if (_cache.TryGetValue(CacheKey, out (string Name, string ShortName) cached))
                return cached;

            var branding = (Name: DefaultSiteName, ShortName: DefaultSiteShortName);

            try
            {
                var settings = await _context.ThemeSettings
                    .AsNoTracking()
                    .Where(t => t.IsActive)
                    .Select(t => new { t.SiteName, t.SiteShortName })
                    .FirstOrDefaultAsync();

                if (settings != null)
                {
                    var name = string.IsNullOrWhiteSpace(settings.SiteName) ? DefaultSiteName : settings.SiteName.Trim();
                    var shortName = string.IsNullOrWhiteSpace(settings.SiteShortName) ? name : settings.SiteShortName.Trim();
                    branding = (name, shortName);
                }
            }
            catch
            {
                // The name is decoration — never let a database hiccup take down every page.
            }

            _cache.Set(CacheKey, branding, CacheDuration);
            return branding;
        }
    }
}
