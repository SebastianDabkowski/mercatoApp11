using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Identity
{
    public class SessionTokenOptions
    {
        public const string DefaultCookieName = ".Mercato.Session";

        [Range(1, 1440)]
        public int TokenLifetimeMinutes { get; set; } = 12 * 60;

        public string CookieName { get; set; } = DefaultCookieName;

        public TimeSpan Lifetime => TimeSpan.FromMinutes(TokenLifetimeMinutes);
    }
}
