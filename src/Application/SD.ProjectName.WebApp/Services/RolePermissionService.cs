using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Services;

public record RolePermissionSummary(string Role, IReadOnlyCollection<string> Permissions, IReadOnlyCollection<string> Modules);

public class RolePermissionService
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RolePermissionService> _logger;

    public RolePermissionService(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IMemoryCache cache,
        ILogger<RolePermissionService> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _cache = cache;
        _logger = logger;
    }

    public async Task EnsureDefaultPermissionsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var definition in RolePermissionDefaults.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var role = await _roleManager.FindByNameAsync(definition.Role);
            if (role == null)
            {
                continue;
            }

            var claims = await _roleManager.GetClaimsAsync(role);
            var existing = claims
                .Where(c => c.Type == PermissionClaims.Type)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var permission in definition.Permissions.Where(Permissions.IsKnown))
            {
                if (existing.Contains(permission))
                {
                    continue;
                }

                var result = await _roleManager.AddClaimAsync(role, PermissionClaims.Create(permission));
                if (!result.Succeeded)
                {
                    _logger.LogWarning("Failed to add permission {Permission} to role {Role}: {Errors}", permission, role.Name, result.Errors);
                }
            }

            _cache.Remove(BuildCacheKey(definition.Role));
        }
    }

    public async Task<IReadOnlyCollection<string>> GetPermissionsForRoleAsync(string roleName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return Array.Empty<string>();
        }

        var normalizedRole = roleName.Trim();
        if (_cache.TryGetValue(BuildCacheKey(normalizedRole), out HashSet<string>? cached) && cached != null)
        {
            return cached;
        }

        var role = await _roleManager.FindByNameAsync(normalizedRole);
        if (role == null)
        {
            return Array.Empty<string>();
        }

        var claims = await _roleManager.GetClaimsAsync(role);
        var permissions = claims
            .Where(c => c.Type == PermissionClaims.Type && Permissions.IsKnown(c.Value))
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _cache.Set(BuildCacheKey(normalizedRole), permissions, TimeSpan.FromMinutes(10));
        return permissions;
    }

    public async Task<IReadOnlyCollection<string>> GetPermissionsForUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(principal);
        if (user == null)
        {
            return Array.Empty<string>();
        }

        var roles = await GetRolesAsync(user, cancellationToken);
        if (roles.Count == 0)
        {
            return Array.Empty<string>();
        }

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fromRole = await GetPermissionsForRoleAsync(role, cancellationToken);
            foreach (var permission in fromRole)
            {
                permissions.Add(permission);
            }
        }

        return permissions;
    }

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return roles.Count == 0
            ? Array.Empty<string>()
            : roles.ToArray();
    }

    public async Task<bool> GrantPermissionAsync(string roleName, string permission, CancellationToken cancellationToken = default)
    {
        if (!Permissions.IsKnown(permission) || string.IsNullOrWhiteSpace(roleName))
        {
            return false;
        }

        var role = await _roleManager.FindByNameAsync(roleName.Trim());
        if (role == null)
        {
            return false;
        }

        var claims = await _roleManager.GetClaimsAsync(role);
        if (claims.Any(c => c.Type == PermissionClaims.Type && c.Value.Equals(permission, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var result = await _roleManager.AddClaimAsync(role, PermissionClaims.Create(permission));
        _cache.Remove(BuildCacheKey(role.Name ?? roleName));
        return result.Succeeded;
    }

    public async Task<bool> RevokePermissionAsync(string roleName, string permission, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return false;
        }

        var role = await _roleManager.FindByNameAsync(roleName.Trim());
        if (role == null)
        {
            return false;
        }

        var claims = await _roleManager.GetClaimsAsync(role);
        var claim = claims.FirstOrDefault(c => c.Type == PermissionClaims.Type && c.Value.Equals(permission, StringComparison.OrdinalIgnoreCase));
        if (claim == null)
        {
            return true;
        }

        var result = await _roleManager.RemoveClaimAsync(role, claim);
        _cache.Remove(BuildCacheKey(role.Name ?? roleName));
        return result.Succeeded;
    }

    public async Task<IReadOnlyCollection<RolePermissionSummary>> GetRoleConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var summaries = new List<RolePermissionSummary>();
        foreach (var role in RolePermissionDefaults.Roles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var permissions = await GetPermissionsForRoleAsync(role, cancellationToken);
            var modules = permissions
                .Select(p => Permissions.TryGet(p, out var definition) ? definition!.Module : "Custom")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            summaries.Add(new RolePermissionSummary(role, permissions.ToArray(), modules));
        }

        return summaries;
    }

    private static string BuildCacheKey(string role) => $"rbac-role::{role}";
}
