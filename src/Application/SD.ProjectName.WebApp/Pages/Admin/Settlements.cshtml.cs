using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin
{
    [Authorize(Roles = AccountTypes.Admin)]
    public class SettlementsModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly SettlementOptions _settlementOptions;
        private readonly TimeZoneInfo _timeZone;

        public SettlementsModel(OrderService orderService, SettlementOptions settlementOptions)
        {
            _orderService = orderService;
            _settlementOptions = settlementOptions;
            _timeZone = ResolveTimeZone(settlementOptions.TimeZone);
        }

        [BindProperty(SupportsGet = true)]
        public int Year { get; set; }

        [BindProperty(SupportsGet = true)]
        public int Month { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SellerId { get; set; }

        public List<SellerMonthlySettlementSummary> Summaries { get; private set; } = new();

        public SellerMonthlySettlementDetail? Detail { get; private set; }

        public DateTimeOffset PeriodStart { get; private set; }

        public DateTimeOffset PeriodEnd { get; private set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            NormalizePeriod();
            var window = ResolveWindow();
            PeriodStart = window.Start;
            PeriodEnd = window.End;

            Summaries = await _orderService.GetMonthlySettlementsAsync(Year, Month, null, cancellationToken);

            if (!string.IsNullOrWhiteSpace(SellerId))
            {
                Detail = await _orderService.GetMonthlySettlementDetailAsync(Year, Month, SellerId, cancellationToken);
                if (Detail != null)
                {
                    PeriodStart = Detail.Summary.PeriodStart;
                    PeriodEnd = Detail.Summary.PeriodEnd;
                }
            }
        }

        public async Task<IActionResult> OnGetExportAsync(CancellationToken cancellationToken)
        {
            NormalizePeriod();
            var csv = await _orderService.ExportMonthlySettlementsAsync(Year, Month, SellerId, cancellationToken);
            var fileName = string.IsNullOrWhiteSpace(SellerId)
                ? $"settlements-{Year}-{Month:00}.csv"
                : $"settlement-{SellerId}-{Year}-{Month:00}.csv";

            return File(csv, "text/csv", fileName);
        }

        private void NormalizePeriod()
        {
            if (Year <= 0 || Month <= 0)
            {
                var now = DateTimeOffset.UtcNow;
                Year = now.Year;
                Month = now.Month;
            }

            Month = Math.Clamp(Month, 1, 12);
            Year = Math.Max(1, Year);
        }

        private (DateTimeOffset Start, DateTimeOffset End) ResolveWindow()
        {
            var closeDay = Math.Clamp(_settlementOptions.CloseDay, 1, 28);
            var anchorLocal = new DateTime(Year, Month, closeDay, 0, 0, 0, DateTimeKind.Unspecified);
            var anchor = new DateTimeOffset(anchorLocal, _timeZone.GetUtcOffset(anchorLocal));
            return (anchor.AddMonths(-1), anchor);
        }

        private static TimeZoneInfo ResolveTimeZone(string? timeZone)
        {
            if (string.IsNullOrWhiteSpace(timeZone))
            {
                return TimeZoneInfo.Utc;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}
