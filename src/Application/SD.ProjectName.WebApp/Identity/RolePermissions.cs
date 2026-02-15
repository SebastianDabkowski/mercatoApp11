using System.Security.Claims;

namespace SD.ProjectName.WebApp.Identity;

public static class PlatformRoles
{
    public const string Buyer = AccountTypes.Buyer;
    public const string Seller = AccountTypes.Seller;
    public const string Admin = AccountTypes.Admin;
    public const string Support = "Support";
    public const string Compliance = "Compliance";

    public static readonly string[] All = [Buyer, Seller, Admin, Support, Compliance];

    public static bool IsValid(string? role) =>
        !string.IsNullOrWhiteSpace(role) &&
        All.Contains(role, StringComparer.OrdinalIgnoreCase);
}

public record PermissionDefinition(string Name, string DisplayName, string Module, string Description);

public static class Permissions
{
    public const string BuyerPortal = "buyer:portal";
    public const string SellerWorkspace = "seller:workspace";
    public const string AdminDashboard = "admin:dashboard";
    public const string AdminCatalog = "admin:catalog";
    public const string AdminUsers = "admin:users";
    public const string AdminSettings = "admin:settings";
    public const string AdminReports = "admin:reports";
    public const string AdminModeration = "admin:moderation";
    public const string AdminSettlements = "admin:settlements";
    public const string AdminAudit = "admin:audit";
    public const string SupportCases = "support:cases";
    public const string SupportQuestions = "support:questions";
    public const string ComplianceRegistry = "compliance:processing-registry";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(BuyerPortal, "Buyer portal", "Buyer", "Access buyer dashboards, orders, addresses, and cases."),
        new(SellerWorkspace, "Seller workspace", "Seller", "Access seller dashboards, catalog, orders, payouts, and settings."),
        new(AdminDashboard, "Admin dashboard", "Admin", "Access the admin overview and widgets."),
        new(AdminCatalog, "Admin catalog & moderation", "Admin", "Manage categories, attributes, and moderate products."),
        new(AdminUsers, "Admin user management", "Admin", "List, inspect, and manage platform users."),
        new(AdminSettings, "Admin configuration", "Admin", "Manage platform settings like tax, features, currencies, integrations, and commissions."),
        new(AdminReports, "Admin reporting & analytics", "Admin", "Access admin reports and analytics screens."),
        new(AdminModeration, "Admin reviews & moderation", "Admin", "Moderate reviews and questions content."),
        new(AdminSettlements, "Admin settlements & payouts", "Admin", "Manage settlements and financial overviews."),
        new(AdminAudit, "Admin audit history", "Admin", "View audit logs and admin actions history."),
        new(SupportCases, "Support cases", "Support", "Handle customer and seller cases, returns, and complaints."),
        new(SupportQuestions, "Support questions", "Support", "Handle product questions and Q&A."),
        new(ComplianceRegistry, "Processing registry", "Compliance", "Manage the processing activity registry.")
    ];

    public static bool IsKnown(string permission) =>
        !string.IsNullOrWhiteSpace(permission) &&
        All.Any(p => p.Name.Equals(permission, StringComparison.OrdinalIgnoreCase));

    public static bool TryGet(string permissionName, out PermissionDefinition? definition)
    {
        definition = All.FirstOrDefault(p => p.Name.Equals(permissionName, StringComparison.OrdinalIgnoreCase));
        return definition != null;
    }
}

public record RolePermissionDefinition(string Role, IReadOnlyCollection<string> Permissions);

public static class RolePermissionDefaults
{
    public static readonly IReadOnlyList<RolePermissionDefinition> All =
    [
        new RolePermissionDefinition(PlatformRoles.Buyer, new[] { Permissions.BuyerPortal }),
        new RolePermissionDefinition(PlatformRoles.Seller, new[] { Permissions.SellerWorkspace }),
        new RolePermissionDefinition(PlatformRoles.Admin, new[]
        {
            Permissions.BuyerPortal,
            Permissions.SellerWorkspace,
            Permissions.AdminDashboard,
            Permissions.AdminCatalog,
            Permissions.AdminUsers,
            Permissions.AdminSettings,
            Permissions.AdminReports,
            Permissions.AdminModeration,
            Permissions.AdminSettlements,
            Permissions.AdminAudit,
            Permissions.SupportCases,
            Permissions.SupportQuestions,
            Permissions.ComplianceRegistry
        }),
        new RolePermissionDefinition(PlatformRoles.Support, new[]
        {
            Permissions.AdminDashboard,
            Permissions.SupportCases,
            Permissions.SupportQuestions,
            Permissions.AdminReports
        }),
        new RolePermissionDefinition(PlatformRoles.Compliance, new[]
        {
            Permissions.AdminDashboard,
            Permissions.ComplianceRegistry
        })
    ];

    public static IEnumerable<string> Roles => All.Select(r => r.Role);
}

public static class PermissionClaims
{
    public const string Type = "permission";

    public static Claim Create(string permission) => new(Type, permission);
}
