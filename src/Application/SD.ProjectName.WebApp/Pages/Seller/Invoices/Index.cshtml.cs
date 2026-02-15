using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages.Seller.Invoices
{
    [Authorize(Policy = Permissions.SellerWorkspace)]
    public class IndexModel : PageModel
    {
        private readonly OrderService _orderService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly InvoiceOptions _invoiceOptions;

        [BindProperty(SupportsGet = true)]
        public int Months { get; set; }

        public int MaxMonths => Math.Max(1, _invoiceOptions.HistoryMonths);

        public List<CommissionInvoiceSummaryView> Invoices { get; private set; } = new();

        public string TaxLabel => _invoiceOptions.TaxLabel;

        public IndexModel(OrderService orderService, UserManager<ApplicationUser> userManager, InvoiceOptions invoiceOptions)
        {
            _orderService = orderService;
            _userManager = userManager;
            _invoiceOptions = invoiceOptions;
            Months = invoiceOptions.HistoryMonths;
        }

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            Months = Math.Clamp(Months <= 0 ? _invoiceOptions.HistoryMonths : Months, 1, MaxMonths);
            Invoices = await _orderService.GetCommissionInvoicesForSellerAsync(sellerId, Months, cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnGetDownloadAsync(string invoiceNumber, CancellationToken cancellationToken)
        {
            var sellerId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(sellerId))
            {
                return Challenge();
            }

            var pdf = await _orderService.GetCommissionInvoicePdfAsync(invoiceNumber, sellerId, cancellationToken);
            if (pdf == null)
            {
                return NotFound();
            }

            return File(pdf.Content, "application/pdf", pdf.FileName);
        }
    }
}
