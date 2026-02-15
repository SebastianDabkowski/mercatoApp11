using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Identity;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly RolePermissionService _rolePermissionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public PermissionAuthorizationHandler(RolePermissionService rolePermissionService, UserManager<ApplicationUser> userManager)
    {
        _rolePermissionService = rolePermissionService;
        _userManager = userManager;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (!Permissions.IsKnown(requirement.Permission))
        {
            return;
        }

        if (context.User.HasClaim(PermissionClaims.Type, requirement.Permission))
        {
            context.Succeed(requirement);
            return;
        }

        var userId = _userManager.GetUserId(context.User);
        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return;
        }

        var roles = await _rolePermissionService.GetRolesAsync(user, CancellationToken.None);
        if (roles.Count == 0)
        {
            return;
        }

        foreach (var role in roles)
        {
            var permissions = await _rolePermissionService.GetPermissionsForRoleAsync(role, CancellationToken.None);
            if (permissions.Contains(requirement.Permission))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}

public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (Permissions.IsKnown(policyName))
        {
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
