using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class AnalyticsOptions
    {
        public const string SectionName = "Analytics";

        /// <summary>
        /// Enables analytics event logging when true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Cookie used to persist anonymous session identifiers for event correlation.
        /// </summary>
        [Required]
        [MinLength(3)]
        public string SessionCookieName { get; set; } = ".Mercato.Analytics";

        /// <summary>
        /// Days to keep the analytics session cookie alive.
        /// </summary>
        [Range(1, 365)]
        public int SessionLifespanDays { get; set; } = 30;

        /// <summary>
        /// Soft cap of events persisted per HTTP request to reduce overhead.
        /// </summary>
        [Range(1, 500)]
        public int MaxEventsPerRequest { get; set; } = 25;
    }
}
