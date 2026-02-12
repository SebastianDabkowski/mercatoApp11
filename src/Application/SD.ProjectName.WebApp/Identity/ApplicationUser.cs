using Microsoft.AspNetCore.Identity;

namespace SD.ProjectName.WebApp.Identity
{
    public static class AccountTypes
    {
        public const string Buyer = "Buyer";
        public const string Seller = "Seller";

        public static readonly string[] Allowed = [Buyer, Seller];

        public static bool IsValid(string? accountType) =>
            !string.IsNullOrWhiteSpace(accountType) &&
            Allowed.Contains(accountType, StringComparer.OrdinalIgnoreCase);
    }

    public static class AccountStatuses
    {
        public const string Unverified = "Unverified";
        public const string Verified = "Verified";
    }

    public static class KycStatuses
    {
        public const string NotRequired = "NotRequired";
        public const string Pending = "Pending";
        public const string Approved = "Approved";

        public static readonly string[] Allowed = [NotRequired, Pending, Approved];
    }

    public class ApplicationUser : IdentityUser
    {
        public string AccountType { get; set; } = AccountTypes.Buyer;

        public string AccountStatus { get; set; } = AccountStatuses.Unverified;

        public string KycStatus { get; set; } = KycStatuses.NotRequired;

        public string FullName { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        public string? BusinessName { get; set; }

        public string? TaxId { get; set; }

        public bool TermsAccepted { get; set; }

        public DateTimeOffset? TermsAcceptedOn { get; set; }

        public DateTimeOffset? EmailVerifiedOn { get; set; }

        public DateTimeOffset? KycSubmittedOn { get; set; }

        public DateTimeOffset? KycApprovedOn { get; set; }
    }
}
