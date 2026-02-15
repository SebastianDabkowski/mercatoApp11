namespace SD.ProjectName.WebApp.Identity;

public static class ComplianceRoles
{
    public const string Compliance = "Compliance";

    public static readonly string[] Allowed = [Compliance];

    public static bool IsCompliance(string? role) =>
        !string.IsNullOrWhiteSpace(role) &&
        Allowed.Contains(role, StringComparer.OrdinalIgnoreCase);
}
