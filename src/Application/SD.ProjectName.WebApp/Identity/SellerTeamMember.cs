using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Identity
{
    public class SellerTeamMember
    {
        public Guid Id { get; set; }

        [Required]
        [StringLength(450)]
        public string StoreOwnerId { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(64)]
        public string Role { get; set; } = SellerInternalRoles.Accounting;

        [Required]
        [StringLength(32)]
        public string Status { get; set; } = SellerInternalUserStatuses.Pending;

        [Required]
        [StringLength(64)]
        public string InvitationCode { get; set; } = Guid.NewGuid().ToString("N");

        public DateTimeOffset InvitedOn { get; set; } = DateTimeOffset.UtcNow;

        public string? AcceptedUserId { get; set; }

        public DateTimeOffset? AcceptedOn { get; set; }

        public DateTimeOffset? DeactivatedOn { get; set; }
    }
}
