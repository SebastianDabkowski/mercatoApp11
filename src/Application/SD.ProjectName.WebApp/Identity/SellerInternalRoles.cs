namespace SD.ProjectName.WebApp.Identity
{
    public static class SellerInternalRoles
    {
        public const string StoreOwner = "StoreOwner";
        public const string CatalogManager = "CatalogManager";
        public const string OrderManager = "OrderManager";
        public const string Accounting = "Accounting";

        public static readonly string[] Allowed =
        [
            StoreOwner,
            CatalogManager,
            OrderManager,
            Accounting
        ];

        public static bool IsValid(string? role) =>
            !string.IsNullOrWhiteSpace(role) &&
            Allowed.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public static class SellerInternalUserStatuses
    {
        public const string Pending = "Pending";
        public const string Active = "Active";
        public const string Deactivated = "Deactivated";
    }
}
