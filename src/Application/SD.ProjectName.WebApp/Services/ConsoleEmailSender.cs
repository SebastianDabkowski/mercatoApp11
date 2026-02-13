using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

namespace SD.ProjectName.WebApp.Services
{
    public class ConsoleEmailSender : IEmailSender
    {
        private readonly ILogger<ConsoleEmailSender> _logger;
        private readonly EmailOptions _options;

        public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger, EmailOptions emailOptions)
        {
            _logger = logger;
            _options = emailOptions;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var sender = string.IsNullOrWhiteSpace(_options.FromName)
                ? _options.FromAddress
                : $"{_options.FromName} <{_options.FromAddress}>";
            _logger.LogInformation("Dispatching email from {Sender} to {Email} with subject {Subject}", sender, email, subject);
            _logger.LogDebug("Email body: {Body}", htmlMessage);
            _logger.LogInformation("Email send enqueued for {Email}", email);
            return Task.CompletedTask;
        }
    }
}
