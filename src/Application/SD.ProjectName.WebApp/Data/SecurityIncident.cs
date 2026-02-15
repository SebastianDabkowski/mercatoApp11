using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Data
{
    public class SecurityIncident
    {
        public int Id { get; set; }

        [MaxLength(64)]
        public string Source { get; set; } = string.Empty;

        [MaxLength(128)]
        public string Rule { get; set; } = string.Empty;

        [MaxLength(32)]
        public string Severity { get; set; } = "Medium";

        [MaxLength(32)]
        public string Status { get; set; } = "New";

        [MaxLength(512)]
        public string? Summary { get; set; }

        public DateTimeOffset DetectedOn { get; set; }

        public DateTimeOffset? LastStatusOn { get; set; }

        [MaxLength(256)]
        public string? LastStatusBy { get; set; }

        [MaxLength(450)]
        public string? LastStatusByUserId { get; set; }

        [MaxLength(1024)]
        public string? ResolutionNotes { get; set; }

        public List<SecurityIncidentStatusChange> StatusChanges { get; set; } = new();
    }
}
