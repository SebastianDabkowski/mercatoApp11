using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class RecentlyViewedOptions
    {
        public const string SectionName = "RecentlyViewed";

        [Required]
        [MinLength(1)]
        public string CookieName { get; set; } = ".Mercato.RecentlyViewed";

        [Range(1, 50)]
        public int MaxItems { get; set; } = 8;

        [Range(1, 180)]
        public int CookieLifespanDays { get; set; } = 30;
    }
}
