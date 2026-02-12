using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Store
{
    public class DetailsModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public DetailsModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public StoreView? Store { get; private set; }

        public IActionResult OnGet(string storeName)
        {
            if (string.IsNullOrWhiteSpace(storeName))
            {
                return NotFound();
            }

            var users = _userManager.Users;
            if (users == null)
            {
                return NotFound();
            }

            Store = users
                .Where(u => u.BusinessName != null && u.BusinessName.Equals(storeName, StringComparison.OrdinalIgnoreCase))
                .Select(u => new StoreView
                {
                    Name = u.BusinessName!,
                    Description = u.StoreDescription ?? string.Empty,
                    LogoPath = u.StoreLogoPath,
                    ContactEmail = string.IsNullOrWhiteSpace(u.ContactEmail) ? u.Email ?? string.Empty : u.ContactEmail,
                    ContactPhone = u.ContactPhone,
                    ContactWebsite = u.ContactWebsite
                })
                .FirstOrDefault();

            if (Store == null)
            {
                return NotFound();
            }

            return Page();
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
