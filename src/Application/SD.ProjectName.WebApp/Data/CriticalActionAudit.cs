namespace SD.ProjectName.WebApp.Data
{
    public class CriticalActionAudit
    {
        public int Id { get; set; }

        public string ActionType { get; set; } = string.Empty;

        public string? ResourceType { get; set; }

        public string? ResourceId { get; set; }

        public string ActorName { get; set; } = "Unknown";

        public string? ActorUserId { get; set; }

        public bool IsSuccess { get; set; }

        public string? Details { get; set; }

        public DateTimeOffset OccurredOn { get; set; }
    }
}
