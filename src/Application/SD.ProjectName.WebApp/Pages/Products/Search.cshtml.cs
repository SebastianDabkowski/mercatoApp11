using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;

namespace SD.ProjectName.WebApp.Pages.Products
{
    public class SearchModel : PageModel
    {
        private readonly GetProducts _getProducts;
        private const int MaxQueryLength = 200;

        public SearchModel(GetProducts getProducts)
        {
            _getProducts = getProducts;
        }

        [BindProperty(SupportsGet = true)]
        public string? Q { get; set; }

        public List<ProductModel> Results { get; private set; } = new();

        public string? StatusMessage { get; private set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(Q))
            {
                StatusMessage = "Enter a keyword to search active products by title or description.";
                return;
            }

            var term = Q.Trim();
            if (term.Length > MaxQueryLength)
            {
                term = term[..MaxQueryLength];
            }

            Q = term;

            Results = await _getProducts.SearchActive(term, cancellationToken);

            if (!Results.Any())
            {
                StatusMessage = "No products match this search. Try adjusting your keywords or browse categories.";
            }
        }
    }
}
