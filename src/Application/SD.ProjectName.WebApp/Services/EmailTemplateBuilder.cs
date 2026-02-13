using System.Globalization;
using System.Text;

namespace SD.ProjectName.WebApp.Services
{
    public static class EmailTemplateBuilder
    {
        public static string Wrap(string heading, string bodyHtml, EmailOptions options, CultureInfo? culture = null)
        {
            var cultureInfo = culture ?? CultureInfo.CurrentCulture;
            var sender = string.IsNullOrWhiteSpace(options.FromName)
                ? options.FromAddress
                : $"{options.FromName} &lt;{options.FromAddress}&gt;";

            var builder = new StringBuilder();
            builder.Append("<div style=\"font-family:Arial, sans-serif; line-height:1.6; color:#111827;\">");
            builder.Append($"<h2 style=\"color:#111827;\">{heading}</h2>");
            builder.Append(bodyHtml);
            builder.Append($"<p style=\"color:#6b7280;font-size:12px;\">This email was sent by {sender}.</p>");
            builder.Append("</div>");
            return builder.ToString();
        }

        public static string FormatCurrency(decimal amount, CultureInfo? culture = null)
        {
            return amount.ToString("C", culture ?? CultureInfo.CurrentCulture);
        }
    }
}
