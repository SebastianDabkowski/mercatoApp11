using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Identity;

public class RolePermissionServiceTests
{
    [Fact]
    public async Task EnsureDefaultPermissionsAsync_AddsMissingClaims()
    {
        var roles = RolePermissionDefaults.Roles.ToDictionary(r => r, r => new IdentityRole(r));
        var roleClaims = roles.Keys.ToDictionary(k => k, _ => new List<Claim>());
        var roleManager = CreateRoleManager(roles, roleClaims);
        var userManager = CreateUserManager();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new RolePermissionService(roleManager.Object, userManager.Object, cache, Mock.Of<ILogger<RolePermissionService>>());

        await service.EnsureDefaultPermissionsAsync();

        foreach (var definition in RolePermissionDefaults.All)
        {
            var claims = roleClaims[definition.Role];
            foreach (var permission in definition.Permissions)
            {
                Assert.Contains(claims, c => c.Type == PermissionClaims.Type && c.Value == permission);
            }
        }
    }

    [Fact]
    public async Task GetPermissionsForUserAsync_MergesAllRolePermissions()
    {
        var roles = new Dictionary<string, IdentityRole>
        {
            { PlatformRoles.Admin, new IdentityRole(PlatformRoles.Admin) },
            { PlatformRoles.Support, new IdentityRole(PlatformRoles.Support) }
        };
        var roleClaims = new Dictionary<string, List<Claim>>
        {
            { PlatformRoles.Admin, new List<Claim> { PermissionClaims.Create(Permissions.AdminDashboard) } },
            { PlatformRoles.Support, new List<Claim> { PermissionClaims.Create(Permissions.SupportCases) } }
        };
        var roleManager = CreateRoleManager(roles, roleClaims);
        var userManager = CreateUserManager();
        var cache = new MemoryCache(new MemoryCacheOptions());

        var user = new ApplicationUser { Id = "user-1", Email = "user@example.com" };
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
        userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { PlatformRoles.Admin, PlatformRoles.Support });

        var service = new RolePermissionService(roleManager.Object, userManager.Object, cache, Mock.Of<ILogger<RolePermissionService>>());

        var permissions = await service.GetPermissionsForUserAsync(new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);

        Assert.Contains(Permissions.AdminDashboard, permissions);
        Assert.Contains(Permissions.SupportCases, permissions);
    }

    private static Mock<RoleManager<IdentityRole>> CreateRoleManager(Dictionary<string, IdentityRole> roles, Dictionary<string, List<Claim>> roleClaims)
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        var manager = new Mock<RoleManager<IdentityRole>>(
            store.Object,
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<ILogger<RoleManager<IdentityRole>>>())
        { CallBase = false };

        manager.Setup(m => m.FindByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((string roleName) =>
            {
                roles.TryGetValue(roleName, out var role);
                return role;
            });

        manager.Setup(m => m.GetClaimsAsync(It.IsAny<IdentityRole>()))
            .ReturnsAsync((IdentityRole role) =>
            {
                if (roleClaims.TryGetValue(role.Name ?? string.Empty, out var claims))
                {
                    return claims;
                }

                return new List<Claim>();
            });

        manager.Setup(m => m.AddClaimAsync(It.IsAny<IdentityRole>(), It.IsAny<Claim>()))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<IdentityRole, Claim>((role, claim) =>
            {
                if (string.IsNullOrEmpty(role.Name))
                {
                    return;
                }

                if (!roleClaims.TryGetValue(role.Name, out var claims))
                {
                    claims = new List<Claim>();
                    roleClaims[role.Name] = claims;
                }

                claims.Add(claim);
            });

        return manager;
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<ApplicationUser>>>())
        { CallBase = false };
    }
}
