using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SD.ProjectName.Modules.Products.Application;
using SD.ProjectName.Modules.Products.Domain;
using SD.ProjectName.Modules.Products.Domain.Interfaces;
using SD.ProjectName.Modules.Products.Infrastructure;
using SD.ProjectName.WebApp.Data;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;
using WebPush;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.User.RequireUniqueEmail = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddRoles<IdentityRole>()
    .AddPasswordValidator<CommonPasswordValidator>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24));
builder.Services.Configure<KycOptions>(builder.Configuration.GetSection("Kyc"));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.Configure<SellerInternalUserOptions>(builder.Configuration.GetSection("SellerInternalUsers"));
builder.Services.AddOptions<SessionTokenOptions>()
    .Bind(builder.Configuration.GetSection("Session"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<SessionTokenOptions>(sp => sp.GetRequiredService<IOptions<SessionTokenOptions>>().Value);
builder.Services.AddOptions<RecentlyViewedOptions>()
    .Bind(builder.Configuration.GetSection(RecentlyViewedOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<RecentlyViewedOptions>(sp => sp.GetRequiredService<IOptions<RecentlyViewedOptions>>().Value);
builder.Services.AddOptions<CartOptions>()
    .Bind(builder.Configuration.GetSection(CartOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<CartOptions>(sp => sp.GetRequiredService<IOptions<CartOptions>>().Value);
builder.Services.AddOptions<PromoOptions>()
    .Bind(builder.Configuration.GetSection(PromoOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<PromoOptions>(sp => sp.GetRequiredService<IOptions<PromoOptions>>().Value);
builder.Services.AddOptions<CheckoutOptions>()
    .Bind(builder.Configuration.GetSection(CheckoutOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<CheckoutOptions>(sp => sp.GetRequiredService<IOptions<CheckoutOptions>>().Value);
builder.Services.AddOptions<ShippingProviderOptions>()
    .Bind(builder.Configuration.GetSection(ShippingProviderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<ShippingProviderOptions>(sp => sp.GetRequiredService<IOptions<ShippingProviderOptions>>().Value);
builder.Services.AddOptions<ShippingAddressOptions>()
    .Bind(builder.Configuration.GetSection(ShippingAddressOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<ShippingAddressOptions>(sp => sp.GetRequiredService<IOptions<ShippingAddressOptions>>().Value);
builder.Services.AddOptions<EscrowOptions>()
    .Bind(builder.Configuration.GetSection(EscrowOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<EscrowOptions>(sp => sp.GetRequiredService<IOptions<EscrowOptions>>().Value);
builder.Services.AddOptions<SettlementOptions>()
    .Bind(builder.Configuration.GetSection(SettlementOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<SettlementOptions>(sp => sp.GetRequiredService<IOptions<SettlementOptions>>().Value);
builder.Services.AddOptions<AdminReportOptions>()
    .Bind(builder.Configuration.GetSection(AdminReportOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<AdminReportOptions>(sp => sp.GetRequiredService<IOptions<AdminReportOptions>>().Value);
builder.Services.AddOptions<InvoiceOptions>()
    .Bind(builder.Configuration.GetSection(InvoiceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<InvoiceOptions>(sp => sp.GetRequiredService<IOptions<InvoiceOptions>>().Value);
builder.Services.AddOptions<CaseSlaOptions>()
    .Bind(builder.Configuration.GetSection(CaseSlaOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<CaseSlaOptions>(sp => sp.GetRequiredService<IOptions<CaseSlaOptions>>().Value);
builder.Services.AddOptions<PaymentProviderOptions>()
    .Bind(builder.Configuration.GetSection(PaymentProviderOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<PaymentProviderOptions>(sp => sp.GetRequiredService<IOptions<PaymentProviderOptions>>().Value);
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<EmailOptions>(sp => sp.GetRequiredService<IOptions<EmailOptions>>().Value);
builder.Services.AddOptions<AnalyticsOptions>()
    .Bind(builder.Configuration.GetSection(AnalyticsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<AnalyticsOptions>(sp => sp.GetRequiredService<IOptions<AnalyticsOptions>>().Value);
builder.Services.AddSingleton(sp =>
{
    var options = new PushNotificationOptions();
    builder.Configuration.GetSection(PushNotificationOptions.SectionName).Bind(options);
    options.Subject ??= "mailto:support@mercato.test";

    if (string.IsNullOrWhiteSpace(options.PublicKey) || string.IsNullOrWhiteSpace(options.PrivateKey))
    {
        var keys = VapidHelper.GenerateVapidKeys();
        options.PublicKey ??= keys.PublicKey;
        options.PrivateKey ??= keys.PrivateKey;
    }

    return options;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<PushSubscriptionStore>();
builder.Services.AddSingleton<WebPushClient>();
builder.Services.AddSingleton<IPushNotificationDispatcher, PushNotificationDispatcher>();
builder.Services.AddScoped<ISessionTokenService, DistributedSessionTokenService>();
builder.Services.AddScoped<ILoginAuditService, LoginAuditService>();
builder.Services.AddScoped<SessionCookieEvents>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, LoggingAuthorizationMiddlewareResultHandler>();
builder.Services.AddSingleton<IPayoutEncryptionService, PayoutEncryptionService>();
builder.Services.AddScoped<RecentlyViewedService>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<CartTotalsCalculator>();
builder.Services.AddScoped<CartViewService>();
builder.Services.AddScoped<PromoCodeService>();
builder.Services.AddScoped<IUserCartService, UserCartService>();
builder.Services.AddScoped<CheckoutStateService>();
builder.Services.AddScoped<ShippingOptionsService>();
builder.Services.AddScoped<ShippingAddressService>();
builder.Services.AddScoped<SellerShippingMethodService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<AdminReportingService>();
builder.Services.AddScoped<SellerReportingService>();
builder.Services.AddScoped<IAnalyticsTracker, AnalyticsTracker>();
builder.Services.AddSingleton<PaymentProviderService>();
builder.Services.AddSingleton<ShippingProviderService>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.EventsType = typeof(SessionCookieEvents);
});

var authenticationBuilder = builder.Services.AddAuthentication();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SaveTokens = true;
    });
}

var facebookAppId = builder.Configuration["Authentication:Facebook:AppId"];
var facebookAppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];
if (!string.IsNullOrWhiteSpace(facebookAppId) && !string.IsNullOrWhiteSpace(facebookAppSecret))
{
    authenticationBuilder.AddFacebook(options =>
    {
        options.AppId = facebookAppId;
        options.AppSecret = facebookAppSecret;
        options.SaveTokens = true;
        options.Fields.Add("email");
        options.Fields.Add("name");
    });
}

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AccountTypes.Buyer, policy => policy.RequireRole(AccountTypes.Buyer));
    options.AddPolicy(AccountTypes.Seller, policy => policy.RequireRole(AccountTypes.Seller));
    options.AddPolicy(AccountTypes.Admin, policy => policy.RequireRole(AccountTypes.Admin));
});

// init module Products
builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<GetProducts>();
builder.Services.AddScoped<CreateProduct>();
builder.Services.AddScoped<UpdateProduct>();
builder.Services.AddScoped<ChangeProductWorkflowState>();
builder.Services.AddScoped<ArchiveProduct>();
builder.Services.AddScoped<BulkUpdateProducts>();
builder.Services.AddScoped<ManageCategories>();
builder.Services.AddSingleton<ProductImportQueue>();
builder.Services.AddScoped<ProductCatalogImportService>();
builder.Services.AddHostedService<ProductImportBackgroundService>();
builder.Services.AddScoped<ProductImageService>();
builder.Services.AddSingleton<ProductExportQueue>();
builder.Services.AddScoped<ProductCatalogExportService>();
builder.Services.AddHostedService<ProductExportBackgroundService>();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Buyer", AccountTypes.Buyer);
    options.Conventions.AuthorizeFolder("/Seller", AccountTypes.Seller);
    options.Conventions.AuthorizeFolder("/Admin", AccountTypes.Admin);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await IdentityDataSeeder.SeedRolesAsync(scope.ServiceProvider);
}

// Apply migrations on startup for all modules
//using (var scope = app.Services.CreateScope())
//{
//    var services = scope.ServiceProvider;
//    try
//    {
//        // Migrate ApplicationDbContext
//        var applicationDbContext = services.GetRequiredService<ApplicationDbContext>();
//        applicationDbContext.Database.Migrate();
//        // Migrate ProductDbContext (Module: Products)
//        var productDbContext = services.GetRequiredService<ProductDbContext>();
//        productDbContext.Database.Migrate();
//    }
//    catch (Exception ex)
//    {
//        var logger = services.GetRequiredService<ILogger<Program>>();
//        logger.LogError(ex, "An error occurred while migrating the database.");
//        throw;
//    }
//}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
