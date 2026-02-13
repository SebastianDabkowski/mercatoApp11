namespace SD.ProjectName.WebApp.Data
{
    public class UserAdminAudit
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string? Reason { get; set; }

        public string? ActorUserId { get; set; }

        public string? ActorName { get; set; }

        public DateTimeOffset CreatedOn { get; set; }
    }
}
