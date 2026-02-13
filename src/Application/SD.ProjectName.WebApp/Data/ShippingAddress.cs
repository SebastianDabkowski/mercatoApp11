using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Data
{
    public class ShippingAddress
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string Recipient { get; set; } = string.Empty;

        public string Line1 { get; set; } = string.Empty;

        public string? Line2 { get; set; }

        public string City { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public string PostalCode { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public bool IsDefault { get; set; }

        public DateTimeOffset CreatedOn { get; set; }

        public DateTimeOffset UpdatedOn { get; set; }

        public ApplicationUser? User { get; set; }
    }
}
