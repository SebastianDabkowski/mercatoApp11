namespace SD.ProjectName.Modules.Products.Domain
{
    public static class ProductModerationStatuses
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Rejected = "rejected";

        public static bool IsValid(string? status) =>
            !string.IsNullOrWhiteSpace(status) &&
            (status.Equals(Pending, StringComparison.OrdinalIgnoreCase) ||
             status.Equals(Approved, StringComparison.OrdinalIgnoreCase) ||
             status.Equals(Rejected, StringComparison.OrdinalIgnoreCase));

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Pending;
            }

            var normalized = status.Trim().ToLowerInvariant();
            return normalized switch
            {
                Approved => Approved,
                Rejected => Rejected,
                _ => Pending
            };
        }
    }
}
