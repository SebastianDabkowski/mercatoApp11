using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Data
{
    public class SecurityIncidentStatusChange
    {
        public int Id { get; set; }

        public int IncidentId { get; set; }

        [MaxLength(32)]
        public string Status { get; set; } = "New";

        [MaxLength(256)]
        public string ActorName { get; set; } = "System";

        [MaxLength(450)]
        public string? ActorUserId { get; set; }

        [MaxLength(512)]
        public string? Notes { get; set; }

        public DateTimeOffset ChangedOn { get; set; }

        public SecurityIncident? Incident { get; set; }
    }
}
