using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class SettlementOptions
    {
        public const string SectionName = "Settlement";

        [Range(1, 28)]
        public int CloseDay { get; set; } = 1;

        [Required]
        public string TimeZone { get; set; } = "UTC";
    }
}
