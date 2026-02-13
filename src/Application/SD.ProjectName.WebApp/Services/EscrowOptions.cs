using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class EscrowOptions
    {
        public const string SectionName = "Escrow";

        [Required]
        public List<string> PayoutEligibleStatuses { get; set; } = new() { OrderStatuses.Delivered };
    }
}
