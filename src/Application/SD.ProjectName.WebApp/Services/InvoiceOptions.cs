using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class InvoiceOptions
    {
        public const string SectionName = "Invoices";

        public string Series { get; set; } = "INV";

        [Range(0, 1)]
        public decimal TaxRate { get; set; } = 0.23m;

        public string TaxLabel { get; set; } = "VAT";

        public string IssuerName { get; set; } = "Mercato";

        public string IssuerTaxId { get; set; } = "TAX-ID";

        public string IssuerAddress { get; set; } = "Market Street 1, City";

        public string IssuerCountry { get; set; } = "PL";

        [Range(1, 36)]
        public int HistoryMonths { get; set; } = 12;

        public string Currency { get; set; } = "USD";
    }
}
