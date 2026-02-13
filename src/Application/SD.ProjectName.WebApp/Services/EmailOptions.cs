using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class EmailOptions
    {
        public const string SectionName = "Email";

        [Required]
        [EmailAddress]
        public string FromAddress { get; set; } = "no-reply@mercato.test";

        [Required]
        public string FromName { get; set; } = "Mercato";

        [EmailAddress]
        public string? SupportAddress { get; set; } = "support@mercato.test";
    }
}
