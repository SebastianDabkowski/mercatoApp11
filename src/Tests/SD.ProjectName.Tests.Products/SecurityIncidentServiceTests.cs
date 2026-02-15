using System.Text;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products;

public class SecurityIncidentServiceTests
{
    [Fact]
    public async Task RecordDetection_CreatesIncidentWithInitialStatus()
    {
        var service = CreateService(out var dbContext, out var emails, clock: new IncidentTimeProvider(DateTimeOffset.UtcNow));

        var result = await service.RecordDetectionAsync(
            new SecurityIncidentDetection("Authentication", "authentication:lockout", "High", "Lockout detected"));

        Assert.True(result.Success);
        Assert.NotNull(result.Incident);
        Assert.Equal(SecurityIncidentStatuses.New, result.Incident!.Status);
        Assert.Equal("High", result.Incident.Severity);
        Assert.Single(dbContext.SecurityIncidents);
        Assert.Single(dbContext.SecurityIncidentStatusChanges);
    }

    [Fact]
    public async Task RecordDetection_SendsAlert_WhenSeverityExceedsThreshold()
    {
        var options = new SecurityIncidentOptions
        {
            AlertSeverityThreshold = "Medium",
            AlertRecipients = new List<string> { "security@example.com" }
        };

        var service = CreateService(out _, out var emails, options);

        var result = await service.RecordDetectionAsync(
            new SecurityIncidentDetection("Authentication", "authentication:lockout", "High", "Lockout detected"));

        Assert.True(result.Success);
        Assert.Single(emails.Messages);
        Assert.Contains("Lockout", emails.Messages[0].Body);
    }

    [Fact]
    public async Task UpdateStatus_AppendsHistoryAndResolution()
    {
        var clock = new IncidentTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(out _, out _, clock: clock);

        var created = await service.RecordDetectionAsync(
            new SecurityIncidentDetection("ApiGateway", "api:abuse", "Medium", "Suspicious usage spike"));
        var incidentId = created.Incident!.Id;

        clock.Advance(TimeSpan.FromHours(1));
        var update = await service.UpdateStatusAsync(
            new SecurityIncidentStatusUpdate(incidentId, SecurityIncidentStatuses.Resolved, "user-1", "Security Officer", "Containment complete"));

        Assert.True(update.Success);
        var history = await service.GetHistoryAsync(incidentId);
        Assert.Contains(history, h => h.Status == SecurityIncidentStatuses.Resolved && h.Notes == "Containment complete");
        var incident = await service.GetAsync(incidentId);
        Assert.Equal(SecurityIncidentStatuses.Resolved, incident!.Status);
        Assert.Equal("Containment complete", incident.ResolutionNotes);
    }

    [Fact]
    public async Task Export_ReturnsCsvWithIncidentsInWindow()
    {
        var clock = new IncidentTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(out _, out _, clock: clock);

        await service.RecordDetectionAsync(
            new SecurityIncidentDetection("Auth", "authentication:unusual-login", "Medium", "New location login"));
        clock.Advance(TimeSpan.FromDays(1));
        await service.RecordDetectionAsync(
            new SecurityIncidentDetection("Data", "data:anomaly", "High", "Data access anomaly"));

        var export = await service.ExportAsync(clock.GetUtcNow().AddDays(-2), clock.GetUtcNow());
        var csv = Encoding.UTF8.GetString(export.Content);

        Assert.Equal(2, export.RowCount);
        Assert.Contains("data:anomaly", csv);
        Assert.Contains("authentication:unusual-login", csv);
    }

    private static SecurityIncidentService CreateService(
        out ApplicationDbContext dbContext,
        out TestEmailSender emailSender,
        SecurityIncidentOptions? options = null,
        IncidentTimeProvider? clock = null)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        dbContext = new ApplicationDbContext(dbOptions);
        emailSender = new TestEmailSender();
        options ??= new SecurityIncidentOptions();
        clock ??= new IncidentTimeProvider(DateTimeOffset.UtcNow);

        return new SecurityIncidentService(
            dbContext,
            options,
            clock,
            emailSender,
            NullLogger<SecurityIncidentService>.Instance);
    }

    private class TestEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Messages { get; } = new();

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Messages.Add((email, subject, htmlMessage));
            return Task.CompletedTask;
        }
    }
}

internal class IncidentTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public IncidentTimeProvider(DateTimeOffset start)
    {
        _utcNow = start;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta)
    {
        _utcNow = _utcNow.Add(delta);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        throw new NotSupportedException();
}
