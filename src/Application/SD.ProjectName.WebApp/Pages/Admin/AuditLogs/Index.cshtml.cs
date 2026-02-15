using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.AuditLogs
{
    [Authorize(Policy = Permissions.AdminAudit)]
    public class IndexModel : PageModel
    {
        private const int DefaultPageSize = 25;
        private readonly AdminAuditLogService _auditLogService;
        private readonly AuditLogOptions _options;

        public IndexModel(AdminAuditLogService auditLogService, AuditLogOptions options)
        {
            _auditLogService = auditLogService;
            _options = options;
        }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        [DataType(DataType.Date)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true, Name = "actor")]
        public string? Actor { get; set; }

        [BindProperty(SupportsGet = true, Name = "entity")]
        public string? EntityType { get; set; }

        [BindProperty(SupportsGet = true, Name = "action")]
        public string? ActionType { get; set; }

        [BindProperty(SupportsGet = true, Name = "resource")]
        public string? ResourceId { get; set; }

        [BindProperty(SupportsGet = true, Name = "result")]
        public string? Result { get; set; }

        [BindProperty(SupportsGet = true, Name = "page")]
        public int PageNumber { get; set; } = 1;

        public PagedResult<AdminAuditLogEntry> Logs { get; private set; } = new()
        {
            Items = new List<AdminAuditLogEntry>(),
            PageNumber = 1,
            PageSize = DefaultPageSize,
            TotalCount = 0
        };

        public IReadOnlyCollection<string> SupportedEntityTypes => _auditLogService.SupportedEntityTypes;

        public int RetentionDays => _options.RetentionDays;

        public bool HasFilters =>
            FromDate.HasValue ||
            ToDate.HasValue ||
            !string.IsNullOrWhiteSpace(Actor) ||
            !string.IsNullOrWhiteSpace(EntityType) ||
            !string.IsNullOrWhiteSpace(ActionType) ||
            !string.IsNullOrWhiteSpace(ResourceId) ||
            !string.IsNullOrWhiteSpace(Result);

        public async Task OnGetAsync()
        {
            var filters = new AdminAuditLogFilters
            {
                From = NormalizeStartOfDay(FromDate),
                To = NormalizeEndOfDay(ToDate),
                Actor = string.IsNullOrWhiteSpace(Actor) ? null : Actor.Trim(),
                EntityType = string.IsNullOrWhiteSpace(EntityType) ? null : EntityType.Trim(),
                ActionType = string.IsNullOrWhiteSpace(ActionType) ? null : ActionType.Trim(),
                ResourceId = string.IsNullOrWhiteSpace(ResourceId) ? null : ResourceId.Trim(),
                IsSuccess = NormalizeResult(Result)
            };

            PageNumber = Math.Max(1, PageNumber);
            Logs = await _auditLogService.GetAsync(filters, PageNumber, DefaultPageSize, HttpContext.RequestAborted);
            PageNumber = Logs.PageNumber <= 0 ? 1 : Logs.PageNumber;
        }

        private static DateTimeOffset? NormalizeStartOfDay(DateTime? date)
        {
            if (!date.HasValue)
            {
                return null;
            }

            var unspecified = DateTime.SpecifyKind(date.Value, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, TimeSpan.Zero);
        }

        private static DateTimeOffset? NormalizeEndOfDay(DateTime? date)
        {
            if (!date.HasValue)
            {
                return null;
            }

            var unspecified = DateTime.SpecifyKind(date.Value, DateTimeKind.Unspecified)
                .AddDays(1)
                .AddTicks(-1);
            return new DateTimeOffset(unspecified, TimeSpan.Zero);
        }

        private static bool? NormalizeResult(string? result) =>
            result?.Trim().ToLowerInvariant() switch
            {
                "success" => true,
                "failure" => false,
                "fail" => false,
                _ => null
            };
    }
}
