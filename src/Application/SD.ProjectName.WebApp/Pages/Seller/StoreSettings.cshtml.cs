using System.ComponentModel.DataAnnotations;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using SD.ProjectName.WebApp.Identity;

namespace SD.ProjectName.WebApp.Pages.Seller
{
    [Authorize(Policy = Permissions.SellerWorkspace)]
    public class StoreSettingsModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<StoreSettingsModel> _logger;

        public StoreSettingsModel(
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            ILogger<StoreSettingsModel> logger)
        {
            _userManager = userManager;
            _environment = environment;
            _logger = logger;
        }

        [BindProperty]
        public StoreProfileInput Input { get; set; } = new();

        public string? CurrentLogoPath { get; private set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            PopulateFromUser(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            CurrentLogoPath = user.StoreLogoPath;
            if (!ModelState.IsValid)
            {
                return Page();
            }

            Input.StoreName = Input.StoreName.Trim();
            Input.StoreDescription = Input.StoreDescription?.Trim() ?? string.Empty;
            Input.ContactEmail = Input.ContactEmail.Trim();
            Input.ContactPhone = string.IsNullOrWhiteSpace(Input.ContactPhone) ? null : Input.ContactPhone.Trim();
            Input.ContactWebsite = string.IsNullOrWhiteSpace(Input.ContactWebsite) ? null : Input.ContactWebsite.Trim();

            var users = _userManager.Users;
            var duplicateName = users != null && users.Any(u =>
                u.Id != user.Id &&
                !string.IsNullOrEmpty(u.BusinessName) &&
                u.BusinessName.Equals(Input.StoreName, StringComparison.OrdinalIgnoreCase));

            if (duplicateName)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.StoreName)}", "Store name is already taken. Choose another.");
                return Page();
            }

            if (Input.LogoFile != null)
            {
                var validationError = ValidateLogo(Input.LogoFile);
                if (!string.IsNullOrEmpty(validationError))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.LogoFile)}", validationError);
                    return Page();
                }

                user.StoreLogoPath = await SaveLogoAsync(Input.LogoFile, user.Id);
                CurrentLogoPath = user.StoreLogoPath;
            }

            user.BusinessName = Input.StoreName;
            user.StoreDescription = Input.StoreDescription;
            user.ContactEmail = Input.ContactEmail;
            user.ContactPhone = Input.ContactPhone;
            user.ContactWebsite = Input.ContactWebsite;

            await _userManager.UpdateAsync(user);
            StatusMessage = "Store profile updated.";
            return RedirectToPage();
        }

        private void PopulateFromUser(ApplicationUser user)
        {
            CurrentLogoPath = user.StoreLogoPath;
            Input = new StoreProfileInput
            {
                StoreName = user.BusinessName ?? string.Empty,
                StoreDescription = user.StoreDescription ?? string.Empty,
                ContactEmail = string.IsNullOrWhiteSpace(user.ContactEmail) ? user.Email ?? string.Empty : user.ContactEmail,
                ContactPhone = user.ContactPhone,
                ContactWebsite = user.ContactWebsite
            };
        }

        private static string? ValidateLogo(IFormFile file)
        {
            var allowedExtensions = new[] { ".png", ".jpg", ".jpeg" };
            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return "Logo must be a PNG or JPEG image.";
            }

            if (file.Length <= 0)
            {
                return "Uploaded logo is empty.";
            }

            const long maxBytes = 2 * 1024 * 1024;
            if (file.Length > maxBytes)
            {
                return "Logo must be 2 MB or smaller.";
            }

            return null;
        }

        private async Task<string> SaveLogoAsync(IFormFile file, string userId)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var root = !string.IsNullOrEmpty(_environment.WebRootPath)
                ? _environment.WebRootPath
                : Path.Combine(_environment.ContentRootPath, "wwwroot");
            var uploadsFolder = Path.Combine(root, "uploads", "store-logos");
            Directory.CreateDirectory(uploadsFolder);
            var fileName = $"{userId}_logo{extension}";
            var destinationPath = Path.Combine(uploadsFolder, fileName);

            await using var fileStream = System.IO.File.Create(destinationPath);
            await file.CopyToAsync(fileStream);

            _logger.LogInformation("Stored logo for user {UserId} at {Path}", userId, destinationPath);

            return $"/uploads/store-logos/{fileName}";
        }

        public class StoreProfileInput
        {
            [Required]
            [Display(Name = "Store name")]
            [StringLength(256)]
            public string StoreName { get; set; } = string.Empty;

            [Required]
            [Display(Name = "Store description")]
            [StringLength(2048)]
            public string StoreDescription { get; set; } = string.Empty;

            [Required]
            [Display(Name = "Contact email")]
            [EmailAddress]
            [StringLength(256)]
            public string ContactEmail { get; set; } = string.Empty;

            [Display(Name = "Contact phone")]
            [Phone]
            [StringLength(64)]
            public string? ContactPhone { get; set; }

            [Display(Name = "Website URL")]
            [Url]
            [StringLength(256)]
            public string? ContactWebsite { get; set; }

            [Display(Name = "Store logo")]
            public IFormFile? LogoFile { get; set; }
        }
    }
}
