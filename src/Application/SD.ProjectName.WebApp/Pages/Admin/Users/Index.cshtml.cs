using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Users
{
    [Authorize(Policy = Permissions.AdminUsers)]
    public class IndexModel : PageModel
    {
        private readonly AdminUserService _userService;
        private const int DefaultPageSize = 20;

        [BindProperty(SupportsGet = true)]
        public string? Query { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Role { get; set; }

        [BindProperty(SupportsGet = true, Name = "status")]
        public List<string> Statuses { get; set; } = new();

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public PagedResult<AdminUserListItem> Users { get; private set; } = new()
        {
            Items = new List<AdminUserListItem>(),
            PageNumber = 1,
            PageSize = DefaultPageSize,
            TotalCount = 0
        };

        public List<string> AvailableRoles { get; } = AccountTypes.Allowed.ToList();

        public List<string> AvailableStatuses { get; } = AdminUserStatuses.All.ToList();

        public bool HasFilters =>
            !string.IsNullOrWhiteSpace(Query) ||
            !string.IsNullOrWhiteSpace(Role) ||
            Statuses.Count > 0;

        public IndexModel(AdminUserService userService)
        {
            _userService = userService;
        }

        public async Task OnGetAsync()
        {
            PageNumber = Math.Max(1, PageNumber);
            var filters = new AdminUserListFilters
            {
                Query = string.IsNullOrWhiteSpace(Query) ? null : Query.Trim(),
                Role = string.IsNullOrWhiteSpace(Role) ? null : Role,
                Statuses = Statuses
            };

            Users = await _userService.GetUsersAsync(filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);
            PageNumber = Users.PageNumber;
        }
    }
}
