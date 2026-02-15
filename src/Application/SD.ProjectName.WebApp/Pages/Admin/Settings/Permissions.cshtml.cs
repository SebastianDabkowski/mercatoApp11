using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Settings;

[Authorize(Policy = Permissions.AdminSettings)]
public class PermissionsModel : PageModel
{
    private readonly RolePermissionService _rolePermissionService;

    public PermissionsModel(RolePermissionService rolePermissionService)
    {
        _rolePermissionService = rolePermissionService;
    }

    public IReadOnlyCollection<RolePermissionSummary> RolePermissions { get; private set; } = Array.Empty<RolePermissionSummary>();

    public IReadOnlyList<PermissionDefinition> PermissionCatalog { get; } = Permissions.All.ToList();

    public IReadOnlyList<string> Roles { get; } = PlatformRoles.All;

    [BindProperty]
    public string SelectedRole { get; set; } = string.Empty;

    [BindProperty]
    public string SelectedPermission { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        RolePermissions = await _rolePermissionService.GetRoleConfigurationAsync(HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostGrantAsync()
    {
        if (!ValidateInput())
        {
            await LoadAsync();
            return Page();
        }

        var updated = await _rolePermissionService.GrantPermissionAsync(SelectedRole, SelectedPermission, HttpContext.RequestAborted);
        if (!updated)
        {
            ModelState.AddModelError(string.Empty, "Unable to assign this permission right now.");
            await LoadAsync();
            return Page();
        }

        StatusMessage = $"Granted {SelectedPermission} to {SelectedRole}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRevokeAsync()
    {
        if (!ValidateInput())
        {
            await LoadAsync();
            return Page();
        }

        var updated = await _rolePermissionService.RevokePermissionAsync(SelectedRole, SelectedPermission, HttpContext.RequestAborted);
        if (!updated)
        {
            ModelState.AddModelError(string.Empty, "Unable to revoke this permission right now.");
            await LoadAsync();
            return Page();
        }

        StatusMessage = $"Revoked {SelectedPermission} from {SelectedRole}.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        RolePermissions = await _rolePermissionService.GetRoleConfigurationAsync(HttpContext.RequestAborted);
    }

    private bool ValidateInput()
    {
        if (!PlatformRoles.IsValid(SelectedRole))
        {
            ModelState.AddModelError(nameof(SelectedRole), "Choose a valid role.");
        }

        if (!Permissions.IsKnown(SelectedPermission))
        {
            ModelState.AddModelError(nameof(SelectedPermission), "Choose a valid permission.");
        }

        return ModelState.IsValid;
    }
}
