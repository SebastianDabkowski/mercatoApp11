using Microsoft.AspNetCore.Identity;

namespace SD.ProjectName.WebApp.Identity
{
    public class SecurityOptions
    {
        /// <summary>
        /// Number of days to retain login history before archival or cleanup.
        /// </summary>
        public int LoginHistoryRetentionDays { get; set; } = 180;

        /// <summary>
        /// When true, users are alerted if a login occurs from a new IP address.
        /// </summary>
        public bool AlertOnNewIp { get; set; } = true;

        /// <summary>
        /// Two-factor provider used for login challenges. Defaults to email codes.
        /// </summary>
        public string TwoFactorProvider { get; set; } = TokenOptions.DefaultEmailProvider;
    }
}
