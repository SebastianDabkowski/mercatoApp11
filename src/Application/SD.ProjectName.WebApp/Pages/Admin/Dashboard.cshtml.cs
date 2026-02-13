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
    [Authorize(Roles = AccountTypes.Admin)]
    public class DashboardModel : PageModel
    {
        private readonly OrderService _orderService;

        [BindProperty(SupportsGet = true)]
        public DateTime? FromDate { get; set; }

        [BindProperty(SupportsGet = true)]
        public DateTime? ToDate { get; set; }

        public List<SellerSlaMetricsView> SlaMetrics { get; private set; } = new();

        public DashboardModel(OrderService orderService)
        {
            _orderService = orderService;
        }

        public async Task OnGetAsync()
        {
            var (from, to) = NormalizeRange();
            SlaMetrics = await _orderService.GetSellerSlaMetricsAsync(from, to, HttpContext.RequestAborted);

            if (!FromDate.HasValue && from.HasValue)
            {
                FromDate = from.Value.Date;
            }

            if (!ToDate.HasValue && to.HasValue)
            {
                ToDate = to.Value.Date;
            }
        }

        private (DateTimeOffset? From, DateTimeOffset? To) NormalizeRange()
        {
            var start = FromDate.HasValue
                ? DateTime.SpecifyKind(FromDate.Value.Date, DateTimeKind.Utc)
                : DateTime.UtcNow.Date.AddDays(-30);
            var end = ToDate.HasValue
                ? DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);

            if (start > end)
            {
                (start, end) = (end, start);
            }

            return (new DateTimeOffset(start), new DateTimeOffset(end));
        }
    }
}
