using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Pages
{
    public class CartModel : PageModel
    {
        private readonly CartViewService _cartViewService;
        private readonly IUserCartService _userCartService;

        public List<CartSellerGroup> SellerGroups { get; private set; } = new();
        public decimal ItemsSubtotal { get; private set; }
        public decimal ShippingTotal { get; private set; }
        public decimal GrandTotal { get; private set; }
        public int TotalQuantity { get; private set; }

        public CartModel(CartViewService cartViewService, IUserCartService userCartService)
        {
            _cartViewService = cartViewService;
            _userCartService = userCartService;
        }

        public async Task OnGet()
        {
            await _userCartService.EnsureUserCartAsync(HttpContext, HttpContext.RequestAborted);
            var summary = await _cartViewService.BuildAsync(HttpContext);
            SellerGroups = summary.SellerGroups;
            ItemsSubtotal = summary.ItemsSubtotal;
            ShippingTotal = summary.ShippingTotal;
            GrandTotal = summary.GrandTotal;
            TotalQuantity = summary.TotalQuantity;
        }
    }
}
