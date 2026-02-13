using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class CartOptions
    {
        public const string SectionName = "Cart";

        [Required]
        [MaxLength(64)]
        public string CookieName { get; set; } = ".SD.Cart";

        [Range(1, 100)]
        public int MaxItems { get; set; } = 50;

        [Range(1, 365)]
        public int CookieLifespanDays { get; set; } = 30;
    }
}
