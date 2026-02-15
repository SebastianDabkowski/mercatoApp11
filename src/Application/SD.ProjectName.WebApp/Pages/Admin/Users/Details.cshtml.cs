using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Users
{
    [Authorize(Policy = Permissions.AdminUsers)]
    public class DetailsModel : PageModel
    {
        private readonly AdminUserService _userService;
        private readonly AdminUserActionService _userActionService;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminUserDetail? UserDetail { get; private set; }
        public List<string> AvailableRoles { get; } = PlatformRoles.All.ToList();

        [BindProperty]
        public BlockInputModel Block { get; set; } = new();

        [BindProperty]
        public string? Role { get; set; }

        [TempData]
        public string? AlertMessage { get; set; }

        public DetailsModel(AdminUserService userService, AdminUserActionService userActionService, UserManager<ApplicationUser> userManager)
        {
            _userService = userService;
            _userActionService = userActionService;
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            UserDetail = await _userService.GetUserAsync(id, HttpContext.RequestAborted);
            if (UserDetail == null)
            {
                return NotFound();
            }

            var actor = await _userManager.GetUserAsync(User);
            var actorName = ResolveActorName(actor);
            await _userActionService.RecordUserAccessAsync(
                id,
                actor?.Id,
                actorName,
                "Viewed profile",
                "Viewed admin user detail",
                HttpContext.RequestAborted);

            Role = UserDetail.Roles.FirstOrDefault();
            return Page();
        }

        public async Task<IActionResult> OnPostBlockAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                UserDetail = await _userService.GetUserAsync(id, HttpContext.RequestAborted);
                Role = UserDetail?.Roles.FirstOrDefault();
                return Page();
            }

            var actor = await _userManager.GetUserAsync(User);
            var actorName = ResolveActorName(actor);
            var result = await _userActionService.BlockUserAsync(id, actor?.Id, actorName, Block.Reason, HttpContext.RequestAborted);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message);
                UserDetail = await _userService.GetUserAsync(id, HttpContext.RequestAborted);
                return Page();
            }

            AlertMessage = result.Message;
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostChangeRoleAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(Role))
            {
                ModelState.AddModelError(nameof(Role), "Choose a platform role.");
                UserDetail = await _userService.GetUserAsync(id, HttpContext.RequestAborted);
                return Page();
            }

            var actor = await _userManager.GetUserAsync(User);
            var actorName = ResolveActorName(actor);
            var result = await _userActionService.ChangeRoleAsync(id, Role, actor?.Id, actorName, HttpContext.RequestAborted);
            if (!result.Success)
            {
                ModelState.AddModelError(nameof(Role), result.Message);
                UserDetail = await _userService.GetUserAsync(id, HttpContext.RequestAborted);
                Role = UserDetail?.Roles.FirstOrDefault();
                return Page();
            }

            AlertMessage = result.Message;
            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnPostUnblockAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var actor = await _userManager.GetUserAsync(User);
            var actorName = ResolveActorName(actor);
            var result = await _userActionService.UnblockUserAsync(id, actor?.Id, actorName, HttpContext.RequestAborted);
            if (!result.Success)
            {
                ModelState.AddModelError(string.Empty, result.Message);
                UserDetail = await _userService.GetUserAsync(id, HttpContext.RequestAborted);
                Role = UserDetail?.Roles.FirstOrDefault();
                return Page();
            }

            AlertMessage = result.Message;
            return RedirectToPage(new { id });
        }

        private static string ResolveActorName(ApplicationUser? actor)
        {
            if (actor == null)
            {
                return "Admin";
            }

            if (!string.IsNullOrWhiteSpace(actor.FullName))
            {
                return actor.FullName;
            }

            if (!string.IsNullOrWhiteSpace(actor.Email))
            {
                return actor.Email;
            }

            return "Admin";
        }

        public class BlockInputModel
        {
            [MaxLength(512)]
            [Display(Name = "Reason for blocking (optional)")]
            public string? Reason { get; set; }
        }
    }
}
