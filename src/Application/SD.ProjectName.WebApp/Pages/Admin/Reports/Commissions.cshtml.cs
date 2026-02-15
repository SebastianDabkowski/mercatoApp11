using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin.Reports
{
    [Authorize(Policy = Permissions.AdminReports)]
    public class CommissionsModel : PageModel
    {
        private readonly OrderService _orderService;

        public CommissionsModel(OrderService orderService)
        {
            _orderService = orderService;
        }

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SellerId { get; set; }

        public IReadOnlyList<CommissionSummaryRow> Summaries { get; private set; } = Array.Empty<CommissionSummaryRow>();

        public CommissionSummaryDetail? Detail { get; private set; }

        public decimal TotalGross { get; private set; }

        public decimal TotalCommission { get; private set; }

        public decimal TotalPayout { get; private set; }

        public DateTimeOffset PeriodStart { get; private set; }

        public DateTimeOffset PeriodEnd { get; private set; }

        public bool HasFilters => FromDate.HasValue || ToDate.HasValue || !string.IsNullOrWhiteSpace(SellerId);

        public async Task OnGetAsync()
        {
            var (start, end) = NormalizeWindow();
            PeriodStart = start;
            PeriodEnd = end;

            Summaries = await _orderService.GetCommissionSummaryAsync(start, end, null, HttpContext.RequestAborted);
            TotalGross = Summaries.Sum(s => s.GrossTotal);
            TotalCommission = Summaries.Sum(s => s.CommissionTotal);
            TotalPayout = Summaries.Sum(s => s.PayoutTotal);

            if (!string.IsNullOrWhiteSpace(SellerId))
            {
                Detail = await _orderService.GetCommissionSummaryDetailAsync(start, end, SellerId, HttpContext.RequestAborted);
            }
        }

        public async Task<IActionResult> OnGetExportAsync()
        {
            var (start, end) = NormalizeWindow();
            var csv = await _orderService.ExportCommissionSummaryAsync(start, end, HttpContext.RequestAborted);
            var fileName = $"commission-summary-{start:yyyyMMdd}-{end:yyyyMMdd}.csv";
            return File(csv, "text/csv", fileName);
        }

        private (DateTimeOffset Start, DateTimeOffset End) NormalizeWindow()
        {
            var today = DateTime.UtcNow.Date;
            var from = FromDate ?? today.AddDays(-30);
            var to = ToDate ?? today;

            if (from > to)
            {
                (from, to) = (to, from);
            }

            var start = new DateTimeOffset(DateTime.SpecifyKind(from.Date, DateTimeKind.Utc));
            var end = new DateTimeOffset(DateTime.SpecifyKind(to.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc));
            return (start, end);
        }
    }
}
