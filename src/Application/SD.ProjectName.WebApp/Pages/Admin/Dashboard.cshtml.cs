using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin
{
    public static class DashboardRangePresets
    {
        public const string Today = "today";
        public const string Last7 = "last7";
        public const string Last30 = "last30";
        public const string Custom = "custom";

        public static bool IsPreset(string? preset) =>
            preset is Today or Last7 or Last30 or Custom;
    }

    [Authorize(Roles = AccountTypes.Admin)]
    public class DashboardModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly AdminReportingService _reportingService;

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Preset { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SelectedMetric { get; set; }

        public DashboardKpiSummary Metrics { get; private set; } = new(0, 0, 0, 0, 0, DateTimeOffset.UtcNow);

        public List<SellerSlaMetricsView> SlaMetrics { get; private set; } = new();

        public IReadOnlyList<DashboardDetailItem> MetricDetails { get; private set; } = Array.Empty<DashboardDetailItem>();

        public DateTimeOffset RangeStart { get; private set; }

        public DateTimeOffset RangeEnd { get; private set; }

        public bool IsEmptyPeriod =>
            Metrics.Orders == 0 && Metrics.TotalGmv <= 0 && Metrics.NewUsers == 0;

        public DashboardModel(OrderService orderService, AdminReportingService reportingService)
        {
            _orderService = orderService;
            _reportingService = reportingService;
        }

        public async Task OnGetAsync()
        {
            var (from, to) = NormalizeRange();
            RangeStart = from;
            RangeEnd = to;
            SelectedMetric = DashboardMetricKeys.IsValid(SelectedMetric?.ToLowerInvariant())
                ? SelectedMetric!.ToLowerInvariant()
                : null;

            var metricResult = await _reportingService.GetDashboardMetricsAsync(from, to, SelectedMetric, HttpContext.RequestAborted);
            Metrics = metricResult.Summary;
            MetricDetails = metricResult.Details;
            SlaMetrics = await _orderService.GetSellerSlaMetricsAsync(from, to, HttpContext.RequestAborted);
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
