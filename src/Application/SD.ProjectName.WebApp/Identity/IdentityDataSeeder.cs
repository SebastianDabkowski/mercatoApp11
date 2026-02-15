using Microsoft.AspNetCore.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Identity
{
    public static class IdentityDataSeeder
    {
        public static async Task SeedRolesAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var rolePermissions = services.GetRequiredService<RolePermissionService>();

            var rolesToEnsure = PlatformRoles.All
                .Concat(SellerInternalRoles.Allowed)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var role in rolesToEnsure)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            await rolePermissions.EnsureDefaultPermissionsAsync(cancellationToken);
        }
    }
}
