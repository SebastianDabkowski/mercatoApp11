using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Data
{
    public class LoginAudit
    {
        public int Id { get; set; }

        public string? UserId { get; set; }

        public string? Email { get; set; }

        public string EventType { get; set; } = string.Empty;

        public bool IsSuccess { get; set; }

        public bool IsUnusual { get; set; }

        public DateTimeOffset OccurredOn { get; set; }

        public DateTimeOffset ExpiresOn { get; set; }

        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        public ApplicationUser? User { get; set; }
    }
}
