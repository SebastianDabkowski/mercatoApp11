using Microsoft.AspNetCore.Identity;

namespace SD.ProjectName.WebApp.Identity
{
    public static class IdentityDataSeeder
    {
        public static async Task SeedRolesAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            foreach (var role in AccountTypes.Allowed
                .Concat(SellerInternalRoles.Allowed)
                .Concat(ComplianceRoles.Allowed))
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
