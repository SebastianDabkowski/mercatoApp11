using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin
{
    [Authorize(Roles = AccountTypes.Admin)]
    public class UserAnalyticsModel : PageModel
    {
        private readonly AdminReportingService _reportingService;

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Preset { get; set; }

        public UserAnalyticsResult Analytics { get; private set; } =
            new(new UserAnalyticsSummary(0, 0, 0, 0, 0, false), Array.Empty<DailyUserAnalyticsPoint>());

        public DateTimeOffset RangeStart { get; private set; }

        public DateTimeOffset RangeEnd { get; private set; }

        public UserAnalyticsModel(AdminReportingService reportingService)
        {
            _reportingService = reportingService;
        }

        public async Task OnGetAsync()
        {
            var (from, to) = NormalizeRange();
            RangeStart = from;
            RangeEnd = to;

            Analytics = await _reportingService.GetUserAnalyticsAsync(from, to, HttpContext.RequestAborted);
        }

        private (DateTimeOffset From, DateTimeOffset To) NormalizeRange()
        {
            var normalizedPreset = DashboardRangePresets.IsPreset(Preset)
                ? Preset!.ToLowerInvariant()
                : DashboardRangePresets.Last30;

            var now = DateTime.UtcNow;
            DateTime start;
            DateTime end;

            switch (normalizedPreset)
            {
                case DashboardRangePresets.Today:
                    start = now.Date;
                    end = now.Date.AddDays(1).AddTicks(-1);
                    break;
                case DashboardRangePresets.Last7:
                    start = now.Date.AddDays(-6);
                    end = now.Date.AddDays(1).AddTicks(-1);
                    break;
                case DashboardRangePresets.Custom:
                    start = FromDate.HasValue
                        ? DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc)
                        : now.Date.AddDays(-30);
                    end = ToDate.HasValue
                        ? DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                        : now.Date.AddDays(1).AddTicks(-1);
                    break;
                case DashboardRangePresets.Last30:
                default:
                    start = now.Date.AddDays(-30);
                    end = now.Date.AddDays(1).AddTicks(-1);
                    break;
            }

            if (start > end)
            {
                (start, end) = (end, start);
            }

            if (!FromDate.HasValue || normalizedPreset != DashboardRangePresets.Custom)
            {
                FromDate = start.Date;
            }

            if (!ToDate.HasValue || normalizedPreset != DashboardRangePresets.Custom)
            {
                ToDate = end.Date;
            }

            Preset = normalizedPreset;

            return (new DateTimeOffset(start), new DateTimeOffset(end));
        }
    }
}
