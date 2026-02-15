using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class SecurityIncidentOptions
    {
        public const string SectionName = "SecurityIncidents";

        [MinLength(1)]
        public List<string> SeverityOrder { get; set; } = new() { "Low", "Medium", "High", "Critical" };

        [Required]
        [MaxLength(32)]
        public string AlertSeverityThreshold { get; set; } = "High";

        public List<string> AlertRecipients { get; set; } = new();

        [MaxLength(32)]
        public string DefaultSeverity { get; set; } = "Medium";

        public Dictionary<string, string> RuleSeverities { get; set; } =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "authentication:lockout", "High" },
                { "authentication:unusual-login", "Medium" }
            };
    }
}
