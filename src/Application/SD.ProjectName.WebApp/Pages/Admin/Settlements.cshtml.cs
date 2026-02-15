using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Admin
{
    [Authorize(Policy = Permissions.AdminSettlements)]
    public class SettlementsModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly SettlementOptions _settlementOptions;
        private readonly AdminUserActionService _userActionService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly TimeZoneInfo _timeZone;

        public SettlementsModel(
            OrderService orderService,
            SettlementOptions settlementOptions,
            AdminUserActionService userActionService,
            UserManager<ApplicationUser> userManager)
        {
            _orderService = orderService;
            _settlementOptions = settlementOptions;
            _userActionService = userActionService;
            _userManager = userManager;
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
                    await LogPayoutAccessAsync(
                        SellerId,
                        "Viewed payout detail",
                        $"Period {Year}-{Month:00}",
                        cancellationToken);
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

            if (!string.IsNullOrWhiteSpace(SellerId))
            {
                await LogPayoutAccessAsync(
                    SellerId,
                    "Exported payout detail",
                    $"Period {Year}-{Month:00}",
                    cancellationToken);
            }

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

        private async Task LogPayoutAccessAsync(string sellerId, string action, string? reason, CancellationToken cancellationToken)
        {
            var actor = await _userManager.GetUserAsync(User);
            var actorName = actor == null
                ? "Admin"
                : (!string.IsNullOrWhiteSpace(actor.FullName) ? actor.FullName : actor.Email ?? actor.UserName ?? "Admin");

            await _userActionService.RecordUserAccessAsync(
                sellerId,
                actor?.Id,
                actorName,
                action,
                reason,
                cancellationToken);
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
