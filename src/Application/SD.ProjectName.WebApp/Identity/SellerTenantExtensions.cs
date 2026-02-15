using System;

namespace SD.ProjectName.WebApp.Identity;

public static class SellerTenantExtensions
{
    public static string GetSellerTenantId(this ApplicationUser user)
    {
        if (user == null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        return string.IsNullOrWhiteSpace(user.StoreOwnerId) ? user.Id : user.StoreOwnerId;
    }
}
