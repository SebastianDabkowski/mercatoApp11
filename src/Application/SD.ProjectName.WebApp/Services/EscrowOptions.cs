using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services
{
    public class EscrowOptions
    {
        public const string SectionName = "Escrow";

        [Required]
        public List<string> PayoutEligibleStatuses { get; set; } = new() { OrderStatuses.Delivered };

        [Required]
        public string DefaultPayoutSchedule { get; set; } = PayoutSchedules.Weekly;

        [Range(0, double.MaxValue)]
        public decimal MinimumPayoutAmount { get; set; } = 10;

        [Range(1, int.MaxValue)]
        public int PayoutBatchSize { get; set; } = 50;
    }
}
