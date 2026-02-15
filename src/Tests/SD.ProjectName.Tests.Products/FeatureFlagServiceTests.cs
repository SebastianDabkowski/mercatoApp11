using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.Tests.Products;

public class FeatureFlagServiceTests
{
    private static FeatureFlagService CreateService(out ApplicationDbContext dbContext)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        dbContext = new ApplicationDbContext(options);
        return new FeatureFlagService(dbContext, TimeProvider.System, NullLogger<FeatureFlagService>.Instance);
    }

    [Fact]
    public async Task Evaluate_ReturnsDefaultEnabled_ForMatchingEnvironment()
    {
        var service = CreateService(out var db);
        await service.SaveAsync(
            new FeatureFlagInput(
                null,
                "search-ui",
                "Search UI",
                "Controls new search page",
                true,
                new List<FeatureFlagEnvironmentInput>
                {
                    new("Production", true, new FeatureFlagTargetingInput(false, new List<string>(), new List<string>(), null))
                }),
            "admin",
            "Admin");

        var result = await service.EvaluateAsync(new FeatureFlagEvaluationContext("search-ui", "Production", null, null, false));

        Assert.True(result);
    }

    [Fact]
    public async Task Evaluate_Blocks_NonInternal_WhenInternalOnly()
    {
        var service = CreateService(out var db);
        await service.SaveAsync(
            new FeatureFlagInput(
                null,
                "beta-checkout",
                "Beta checkout",
                "Rollout to staff only",
                false,
                new List<FeatureFlagEnvironmentInput>
                {
                    new("Staging", true, new FeatureFlagTargetingInput(true, new List<string>(), new List<string>(), null))
                }),
            "admin",
            "Admin");

        var nonInternal = await service.EvaluateAsync(new FeatureFlagEvaluationContext("beta-checkout", "Staging", null, null, false));
        var internalUser = await service.EvaluateAsync(new FeatureFlagEvaluationContext("beta-checkout", "Staging", null, null, true));

        Assert.False(nonInternal);
        Assert.True(internalUser);
    }

    [Fact]
    public async Task Evaluate_Allows_TargetedUser_EvenWhenDisabled()
    {
        var service = CreateService(out var db);
        await service.SaveAsync(
            new FeatureFlagInput(
                null,
                "invite-flow",
                "Invite flow",
                "Targeted allow list",
                false,
                new List<FeatureFlagEnvironmentInput>
                {
                    new("Production", false, new FeatureFlagTargetingInput(false, new List<string> { "user-123" }, new List<string>(), null))
                }),
            "admin",
            "Admin");

        var targeted = await service.EvaluateAsync(new FeatureFlagEvaluationContext("invite-flow", "Production", "user-123", null, false));
        var other = await service.EvaluateAsync(new FeatureFlagEvaluationContext("invite-flow", "Production", "user-999", null, false));

        Assert.True(targeted);
        Assert.False(other);
    }

    [Fact]
    public async Task SetEnvironmentState_TogglesFlag()
    {
        var service = CreateService(out var db);
        var saveResult = await service.SaveAsync(
            new FeatureFlagInput(
                null,
                "seller-portal",
                "Seller portal",
                "Controls seller portal beta",
                false,
                new List<FeatureFlagEnvironmentInput>()),
            "admin",
            "Admin");

        Assert.True(saveResult.Success);

        var toggle = await service.SetEnvironmentStateAsync(saveResult.Flag!.Id, "Production", true, "admin", "Admin");
        Assert.True(toggle.Success);

        var result = await service.EvaluateAsync(new FeatureFlagEvaluationContext("seller-portal", "Production", null, null, false));
        Assert.True(result);
    }
}
