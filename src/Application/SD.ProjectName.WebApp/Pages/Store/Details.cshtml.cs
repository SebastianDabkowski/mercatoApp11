using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using System.Text;

namespace SD.ProjectName.WebApp.Pages.Store
{
    public class DetailsModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly GetProducts _getProducts;

        public DetailsModel(UserManager<ApplicationUser> userManager, GetProducts getProducts)
        {
            _userManager = userManager;
            _getProducts = getProducts;
        }

        public StoreView? Store { get; private set; }

        public List<ProductModel> ProductPreview { get; private set; } = new();

        public bool IsPubliclyVisible { get; private set; }

        public string? StatusMessage { get; private set; }

        public async Task<IActionResult> OnGetAsync(string storeSlug)
        {
            if (string.IsNullOrWhiteSpace(storeSlug))
            {
                return NotFound();
            }

            var slug = NormalizeSlug(storeSlug);
            var users = _userManager.Users;
            if (users == null)
            {
                return NotFound();
            }

            var storeOwner = users
                .AsEnumerable()
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.BusinessName) &&
                                     NormalizeSlug(u.BusinessName!) == slug);

            if (storeOwner == null)
            {
                return NotFound();
            }

            Store = new StoreView
            {
                Name = storeOwner.BusinessName!,
                Description = storeOwner.StoreDescription ?? string.Empty,
                LogoPath = storeOwner.StoreLogoPath,
                ContactEmail = string.IsNullOrWhiteSpace(storeOwner.ContactEmail) ? storeOwner.Email ?? string.Empty : storeOwner.ContactEmail,
                ContactPhone = storeOwner.ContactPhone,
                ContactWebsite = storeOwner.ContactWebsite
            };

            var status = ResolveStoreStatus(storeOwner);
            if (!IsPublic(status))
            {
                StatusMessage = status == StoreStatus.Suspended
                    ? "This store is currently unavailable."
                    : "This store is not yet available for public viewing.";
                Response.StatusCode = status == StoreStatus.Suspended
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status404NotFound;
                return Page();
            }

            IsPubliclyVisible = true;
            var products = await _getProducts.GetList();
            ProductPreview = products.Take(6).ToList();


            return Page();
        }

        private static StoreStatus ResolveStoreStatus(ApplicationUser user)
        {
            if (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value >= DateTimeOffset.UtcNow)
            {
                return StoreStatus.Suspended;
            }

            if (!string.Equals(user.AccountType, AccountTypes.Seller, StringComparison.OrdinalIgnoreCase))
            {
                return StoreStatus.Unverified;
            }

            if (!string.Equals(user.AccountStatus, AccountStatuses.Verified, StringComparison.OrdinalIgnoreCase))
            {
                return StoreStatus.Unverified;
            }

            if (string.Equals(user.OnboardingStatus, OnboardingStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            {
                return StoreStatus.Active;
            }

            if (string.Equals(user.OnboardingStatus, OnboardingStatuses.PendingVerification, StringComparison.OrdinalIgnoreCase))
            {
                return StoreStatus.LimitedActive;
            }

            return StoreStatus.Unverified;
        }

        private static bool IsPublic(StoreStatus status) =>
            status == StoreStatus.Active || status == StoreStatus.LimitedActive;

        private static string NormalizeSlug(string value)
        {
            var trimmed = value.Trim().ToLowerInvariant();
            var builder = new StringBuilder();
            var lastWasHyphen = false;

            foreach (var ch in trimmed)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    lastWasHyphen = false;
                }
                else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_')
                {
                    if (!lastWasHyphen && builder.Length > 0)
                    {
                        builder.Append('-');
                        lastWasHyphen = true;
                    }
                }
            }

            return builder.ToString().Trim('-');
        }

        private enum StoreStatus
        {
            Active,
            LimitedActive,
            Suspended,
            Unverified
        }

        public class StoreView
        {
            public string Name { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;

            public string? LogoPath { get; set; }

            public string ContactEmail { get; set; } = string.Empty;

            public string? ContactPhone { get; set; }

            public string? ContactWebsite { get; set; }
        }
    }
}
