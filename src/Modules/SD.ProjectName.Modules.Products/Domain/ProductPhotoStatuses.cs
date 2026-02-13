namespace SD.ProjectName.Modules.Products.Domain
{
    public static class ProductPhotoStatuses
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Removed = "removed";

        public static bool IsValid(string? status) =>
            !string.IsNullOrWhiteSpace(status) &&
            (status.Equals(Pending, StringComparison.OrdinalIgnoreCase) ||
             status.Equals(Approved, StringComparison.OrdinalIgnoreCase) ||
             status.Equals(Removed, StringComparison.OrdinalIgnoreCase));

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return Pending;
            }

            return status.Trim().ToLowerInvariant() switch
            {
                Approved => Approved,
                Removed => Removed,
                _ => Pending
            };
        }

        public static bool IsApproved(string? status) =>
            Normalize(status) == Approved;
    }
}
