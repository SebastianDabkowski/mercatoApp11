using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SD.ProjectName.WebApp.Identity;
using SD.ProjectName.WebApp.Services;

namespace SD.ProjectName.WebApp.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
        public DbSet<UserAdminAudit> UserAdminAudits => Set<UserAdminAudit>();
        public DbSet<CriticalActionAudit> CriticalActionAudits => Set<CriticalActionAudit>();
        public DbSet<SecurityIncident> SecurityIncidents => Set<SecurityIncident>();
        public DbSet<SecurityIncidentStatusChange> SecurityIncidentStatusChanges => Set<SecurityIncidentStatusChange>();
        public DbSet<SellerTeamMember> SellerTeamMembers => Set<SellerTeamMember>();
        public DbSet<OrderRecord> Orders => Set<OrderRecord>();
        public DbSet<SellerShippingMethod> SellerShippingMethods => Set<SellerShippingMethod>();
        public DbSet<ShippingAddress> ShippingAddresses => Set<ShippingAddress>();
        public DbSet<ProductReview> ProductReviews => Set<ProductReview>();
        public DbSet<SellerRating> SellerRatings => Set<SellerRating>();
        public DbSet<SellerReputation> SellerReputations => Set<SellerReputation>();
        public DbSet<ProductReviewAudit> ProductReviewAudits => Set<ProductReviewAudit>();
        public DbSet<ProductReviewReport> ProductReviewReports => Set<ProductReviewReport>();
        public DbSet<ProductQuestion> ProductQuestions => Set<ProductQuestion>();
        public DbSet<CommissionRule> CommissionRules => Set<CommissionRule>();
        public DbSet<CommissionRuleAudit> CommissionRuleAudits => Set<CommissionRuleAudit>();
        public DbSet<VatRule> VatRules => Set<VatRule>();
        public DbSet<VatRuleAudit> VatRuleAudits => Set<VatRuleAudit>();
        public DbSet<CurrencySetting> CurrencySettings => Set<CurrencySetting>();
        public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
        public DbSet<IntegrationConfiguration> IntegrationConfigurations => Set<IntegrationConfiguration>();
        public DbSet<LegalDocumentVersion> LegalDocumentVersions => Set<LegalDocumentVersion>();
        public DbSet<ConsentDefinition> ConsentDefinitions => Set<ConsentDefinition>();
        public DbSet<ConsentVersion> ConsentVersions => Set<ConsentVersion>();
        public DbSet<UserConsentDecision> UserConsentDecisions => Set<UserConsentDecision>();
        public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
        public DbSet<FeatureFlagEnvironment> FeatureFlagEnvironments => Set<FeatureFlagEnvironment>();
        public DbSet<FeatureFlagAudit> FeatureFlagAudits => Set<FeatureFlagAudit>();
        public DbSet<ProcessingActivity> ProcessingActivities => Set<ProcessingActivity>();
        public DbSet<ProcessingActivityRevision> ProcessingActivityRevisions => Set<ProcessingActivityRevision>();

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(u => u.AccountStatus)
                    .HasMaxLength(32)
                    .HasDefaultValue(AccountStatuses.Unverified);

                entity.Property(u => u.AccountType)
                    .HasMaxLength(32)
                    .IsRequired();

                entity.Property(u => u.StoreOwnerId)
                    .HasMaxLength(450);

                entity.Property(u => u.SellerType)
                    .HasMaxLength(32)
                    .HasDefaultValue(SellerTypes.Individual);

                entity.Property(u => u.KycStatus)
                    .HasMaxLength(32)
                    .HasDefaultValue(KycStatuses.NotRequired);

                entity.Property(u => u.FullName)
                    .HasMaxLength(256)
                    .IsRequired();

                entity.Property(u => u.Address)
                    .HasMaxLength(512)
                    .IsRequired();

                entity.Property(u => u.Country)
                    .HasMaxLength(120)
                    .IsRequired();

                entity.Property(u => u.BusinessName)
                    .HasMaxLength(256);

                entity.HasIndex(u => u.BusinessName)
                    .IsUnique()
                    .HasFilter("[BusinessName] IS NOT NULL");

                entity.Property(u => u.TaxId)
                    .HasMaxLength(128);

                entity.Property(u => u.CompanyRegistrationNumber)
                    .HasMaxLength(128);

                entity.Property(u => u.PersonalIdNumber)
                    .HasMaxLength(128);

                entity.Property(u => u.VerificationContactName)
                    .HasMaxLength(256);

                entity.Property(u => u.EmailVerifiedOn);

                entity.Property(u => u.KycSubmittedOn);

                entity.Property(u => u.KycApprovedOn);

                entity.Property(u => u.TwoFactorMethod)
                    .HasMaxLength(64)
                    .HasDefaultValue(TokenOptions.DefaultEmailProvider);

                entity.Property(u => u.TwoFactorEnabledOn);

                entity.Property(u => u.LastLoginIp)
                    .HasMaxLength(128);

                entity.Property(u => u.LastLoginOn);

                entity.Property(u => u.StoreDescription)
                    .HasMaxLength(2048);

                entity.Property(u => u.ContactEmail)
                    .HasMaxLength(256)
                    .HasDefaultValue(string.Empty);

                entity.Property(u => u.ContactPhone)
                    .HasMaxLength(64);

                entity.Property(u => u.ContactWebsite)
                    .HasMaxLength(256);

                entity.Property(u => u.StoreLogoPath)
                    .HasMaxLength(512);

                entity.Property(u => u.PayoutMethod)
                    .HasMaxLength(64)
                    .HasDefaultValue("BankTransfer");

                entity.Property(u => u.PayoutSchedule)
                    .HasMaxLength(32)
                    .HasDefaultValue("Weekly");

                entity.Property(u => u.PayoutAccount)
                    .HasMaxLength(256);

                entity.Property(u => u.PayoutBankAccount)
                    .HasMaxLength(256);

                entity.Property(u => u.PayoutBankRouting)
                    .HasMaxLength(128);

                entity.Property(u => u.PayoutUpdatedOn);

                entity.Property(u => u.OnboardingStatus)
                    .HasMaxLength(64)
                    .HasDefaultValue(OnboardingStatuses.NotStarted);

                entity.Property(u => u.OnboardingStep)
                    .HasDefaultValue(0);

                entity.Property(u => u.OnboardingStartedOn);

                entity.Property(u => u.OnboardingCompletedOn);

                entity.HasIndex(u => u.StoreOwnerId);

                entity.Property(u => u.CartData)
                    .HasColumnType("nvarchar(max)");

                entity.Property(u => u.BlockedOn);

                entity.Property(u => u.BlockedByUserId)
                    .HasMaxLength(450);

                entity.Property(u => u.BlockedByName)
                    .HasMaxLength(256);

                entity.Property(u => u.BlockReason)
                    .HasMaxLength(512);
            });

            builder.Entity<SecurityIncident>(entity =>
            {
                entity.Property(i => i.Source)
                    .IsRequired()
                    .HasMaxLength(64);

                entity.Property(i => i.Rule)
                    .IsRequired()
                    .HasMaxLength(128);

                entity.Property(i => i.Severity)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(i => i.Status)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(i => i.Summary)
                    .HasMaxLength(512);

                entity.Property(i => i.LastStatusBy)
                    .HasMaxLength(256);

                entity.Property(i => i.LastStatusByUserId)
                    .HasMaxLength(450);

                entity.Property(i => i.ResolutionNotes)
                    .HasMaxLength(1024);

                entity.HasMany(i => i.StatusChanges)
                    .WithOne(sc => sc.Incident)
                    .HasForeignKey(sc => sc.IncidentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<SecurityIncidentStatusChange>(entity =>
            {
                entity.Property(s => s.Status)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(s => s.ActorName)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(s => s.ActorUserId)
                    .HasMaxLength(450);

                entity.Property(s => s.Notes)
                    .HasMaxLength(512);
            });

            builder.Entity<SellerTeamMember>(entity =>
            {
                entity.HasKey(m => m.Id);
                entity.Property(m => m.StoreOwnerId)
                    .IsRequired()
                    .HasMaxLength(450);
                entity.Property(m => m.Email)
                    .IsRequired()
                    .HasMaxLength(256);
                entity.Property(m => m.Role)
                    .IsRequired()
                    .HasMaxLength(64);
                entity.Property(m => m.Status)
                    .IsRequired()
                    .HasMaxLength(32);
                entity.Property(m => m.InvitationCode)
                    .IsRequired()
                    .HasMaxLength(64);
                entity.Property(m => m.InvitedOn)
                    .IsRequired();
                entity.HasIndex(m => m.InvitationCode)
                    .IsUnique();
                entity.HasIndex(m => new { m.StoreOwnerId, m.Email });
            });

            builder.Entity<LoginAudit>(entity =>
            {
                entity.Property(e => e.EventType)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(e => e.Email)
                    .HasMaxLength(256);

                entity.Property(e => e.IpAddress)
                    .HasMaxLength(128);

                entity.Property(e => e.UserAgent)
                    .HasMaxLength(512);

                entity.Property(e => e.OccurredOn)
                    .IsRequired();

                entity.Property(e => e.ExpiresOn)
                    .IsRequired();

                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.OccurredOn);
            });

            builder.Entity<SellerShippingMethod>(entity =>
            {
                entity.HasKey(m => m.Id);
                entity.Property(m => m.StoreOwnerId)
                    .IsRequired()
                    .HasMaxLength(450);
                entity.Property(m => m.Name)
                    .IsRequired()
                    .HasMaxLength(128);
                entity.Property(m => m.Description)
                    .HasMaxLength(1024);
                entity.Property(m => m.BaseCost)
                    .HasColumnType("decimal(18,2)")
                    .HasDefaultValue(0m);
                entity.Property(m => m.DeliveryEstimate)
                    .HasMaxLength(128);
                entity.Property(m => m.Availability)
                    .HasMaxLength(256);
                entity.Property(m => m.ProviderId)
                    .HasMaxLength(64);
                entity.Property(m => m.ProviderServiceCode)
                    .HasMaxLength(64);
                entity.Property(m => m.IsActive)
                    .IsRequired();
                entity.Property(m => m.IsDeleted)
                    .IsRequired();
                entity.Property(m => m.CreatedOn)
                    .IsRequired();
                entity.Property(m => m.UpdatedOn)
                    .IsRequired();
                entity.HasIndex(m => new { m.StoreOwnerId, m.IsDeleted });
            });

            builder.Entity<ShippingAddress>(entity =>
            {
                entity.Property(a => a.UserId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(a => a.Recipient)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(a => a.Line1)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(a => a.Line2)
                    .HasMaxLength(256);

                entity.Property(a => a.City)
                    .HasMaxLength(128);

                entity.Property(a => a.State)
                    .HasMaxLength(128);

                entity.Property(a => a.PostalCode)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(a => a.Country)
                    .IsRequired()
                    .HasMaxLength(120);

                entity.Property(a => a.Phone)
                    .IsRequired()
                    .HasMaxLength(32);

                entity.Property(a => a.CreatedOn)
                    .IsRequired();

                entity.Property(a => a.UpdatedOn)
                    .IsRequired();

                entity.HasOne(a => a.User)
                    .WithMany()
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(a => new { a.UserId, a.IsDefault })
                    .HasFilter("[IsDefault] = 1")
                    .IsUnique();

                entity.HasIndex(a => new { a.UserId, a.CreatedOn });
            });

            builder.Entity<OrderRecord>(entity =>
            {
                entity.Property(o => o.OrderNumber)
                    .HasMaxLength(40)
                    .IsRequired();

                entity.Property(o => o.Status)
                    .HasMaxLength(32)
                    .IsRequired();

                entity.Property(o => o.BuyerId)
                    .HasMaxLength(450);

                entity.Property(o => o.BuyerEmail)
                    .HasMaxLength(256);

                entity.Property(o => o.BuyerName)
                    .HasMaxLength(256);

                entity.Property(o => o.PaymentMethodId)
                    .HasMaxLength(64);

                entity.Property(o => o.PaymentMethodLabel)
                    .HasMaxLength(128);

                entity.Property(o => o.PaymentReference)
                    .HasMaxLength(128);

                entity.Property(o => o.CartSignature)
                    .HasMaxLength(256);

                entity.Property(o => o.SavedAddressKey)
                    .HasMaxLength(64);

                entity.Property(o => o.DeliveryAddressJson)
                    .IsRequired();

                entity.Property(o => o.DetailsJson)
                    .IsRequired();

                entity.HasIndex(o => o.OrderNumber)
                    .IsUnique();

                entity.HasIndex(o => o.PaymentReference)
                    .IsUnique()
                    .HasFilter("[PaymentReference] IS NOT NULL");

                entity.HasIndex(o => new { o.BuyerId, o.Status, o.CreatedOn });
                entity.HasIndex(o => o.Status);
                entity.HasIndex(o => o.CreatedOn);
            });

            builder.Entity<ProductReview>(entity =>
            {
                entity.Property(r => r.BuyerId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.BuyerName)
                    .HasMaxLength(256)
                    .IsRequired();

                entity.Property(r => r.Comment)
                    .HasMaxLength(2000);

                entity.Property(r => r.Status)
                    .HasMaxLength(32)
                    .IsRequired();

                entity.Property(r => r.FlagReason)
                    .HasMaxLength(512);

                entity.Property(r => r.LastModeratedBy)
                    .HasMaxLength(256);

                entity.Property(r => r.CreatedOn)
                    .IsRequired();

                entity.Property(r => r.Rating)
                    .IsRequired();

                entity.HasIndex(r => new { r.OrderId, r.ProductId, r.BuyerId })
                    .IsUnique();

                entity.HasIndex(r => new { r.ProductId, r.Status, r.CreatedOn });
            });

            builder.Entity<ProductReviewAudit>(entity =>
            {
                entity.Property(a => a.Action)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(a => a.Actor)
                    .HasMaxLength(256);

                entity.Property(a => a.Reason)
                    .HasMaxLength(512);

                entity.Property(a => a.FromStatus)
                    .HasMaxLength(32);

                entity.Property(a => a.ToStatus)
                    .HasMaxLength(32);

                entity.Property(a => a.CreatedOn)
                    .IsRequired();

                entity.HasIndex(a => a.ReviewId);
            });

            builder.Entity<ProductReviewReport>(entity =>
            {
                entity.Property(r => r.ReporterId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.Reason)
                    .HasMaxLength(128)
                    .IsRequired();

                entity.Property(r => r.Details)
                    .HasMaxLength(2000);

                entity.Property(r => r.CreatedOn)
                    .IsRequired();

                entity.HasIndex(r => new { r.ReviewId, r.ReporterId })
                    .IsUnique();
            });

            builder.Entity<ProductQuestion>(entity =>
            {
                entity.Property(q => q.ProductId)
                    .IsRequired();

                entity.Property(q => q.SellerId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(q => q.BuyerId)
                    .IsRequired()
                    .HasMaxLength(450);

                entity.Property(q => q.BuyerName)
                    .IsRequired()
                    .HasMaxLength(256);

                entity.Property(q => q.Question)
                    .IsRequired()
                    .HasMaxLength(2000);

                entity.Property(q => q.Answer)
                    .HasMaxLength(2000);

                entity.Property(q => q.Status)
                    .IsRequired()
                    .HasMaxLength(32)
                    .HasDefaultValue(ProductQuestionStatuses.Open);

                entity.Property(q => q.CreatedOn)
                    .IsRequired();

                entity.HasIndex(q => new { q.ProductId, q.Status, q.CreatedOn });
                entity.HasIndex(q => q.SellerId);
            });

            builder.Entity<SellerRating>(entity =>
            {
                entity.Property(r => r.SellerId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.BuyerId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.BuyerName)
                    .HasMaxLength(256)
                    .IsRequired();

                entity.Property(r => r.SellerName)
                    .HasMaxLength(256)
                    .IsRequired();

                entity.Property(r => r.Rating)
                    .IsRequired();

                entity.Property(r => r.Status)
                    .HasMaxLength(32)
                    .IsRequired()
                    .HasDefaultValue(ReviewStatuses.Published);

                entity.Property(r => r.FlagReason)
                    .HasMaxLength(512);

                entity.Property(r => r.LastModeratedBy)
                    .HasMaxLength(256);

                entity.Property(r => r.CreatedOn)
                    .IsRequired();

                entity.HasIndex(r => new { r.OrderId, r.SellerId, r.BuyerId })
                    .IsUnique();

                entity.HasIndex(r => new { r.SellerId, r.Status, r.CreatedOn });
            });

            builder.Entity<SellerReputation>(entity =>
            {
                entity.Property(r => r.SellerId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(r => r.Label)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(r => r.CalculatedOn)
                    .IsRequired();

                entity.HasIndex(r => r.SellerId)
                    .IsUnique();
            });

            builder.Entity<UserAdminAudit>(entity =>
            {
                entity.Property(a => a.UserId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(a => a.ActorUserId)
                    .HasMaxLength(450);

                entity.Property(a => a.ActorName)
                    .HasMaxLength(256);

                entity.Property(a => a.Action)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(a => a.Reason)
                    .HasMaxLength(512);

                entity.Property(a => a.CreatedOn)
                    .IsRequired();

                entity.HasIndex(a => a.UserId);
                entity.HasIndex(a => new { a.UserId, a.CreatedOn });
            });

            builder.Entity<CriticalActionAudit>(entity =>
            {
                entity.Property(a => a.ActionType)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(a => a.ResourceType)
                    .HasMaxLength(64);

                entity.Property(a => a.ResourceId)
                    .HasMaxLength(128);

                entity.Property(a => a.ActorName)
                    .HasMaxLength(256)
                    .IsRequired();

                entity.Property(a => a.ActorUserId)
                    .HasMaxLength(450);

                entity.Property(a => a.Details)
                    .HasMaxLength(512);

                entity.Property(a => a.OccurredOn)
                    .IsRequired();

                entity.HasIndex(a => a.OccurredOn);
                entity.HasIndex(a => new { a.ActionType, a.OccurredOn });
                entity.HasIndex(a => new { a.ResourceType, a.ResourceId, a.OccurredOn });
            });

            builder.Entity<AnalyticsEvent>(entity =>
            {
                entity.Property(e => e.EventType)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(e => e.SessionId)
                    .HasMaxLength(64);

                entity.Property(e => e.UserId)
                    .HasMaxLength(450);

                entity.Property(e => e.SellerId)
                    .HasMaxLength(450);

                entity.Property(e => e.Keyword)
                    .HasMaxLength(256);

                entity.Property(e => e.MetadataJson)
                    .HasColumnType("nvarchar(max)");

                entity.Property(e => e.OccurredOn)
                    .IsRequired();

                entity.HasIndex(e => e.EventType);
                entity.HasIndex(e => e.OccurredOn);
                entity.HasIndex(e => new { e.EventType, e.OccurredOn });
            });

            builder.Entity<CommissionRule>(entity =>
            {
                entity.Property(r => r.Name)
                    .HasDefaultValue(string.Empty)
                    .HasMaxLength(256);
                entity.Property(r => r.Rate)
                    .HasColumnType("decimal(18,4)");
                entity.HasIndex(r => new { r.SellerType, r.Category, r.EffectiveFrom });
            });

            builder.Entity<CommissionRuleAudit>(entity =>
            {
                entity.Property(a => a.Action)
                    .HasDefaultValue("Updated")
                    .HasMaxLength(32);
                entity.HasIndex(a => a.RuleId);
                entity.HasOne<CommissionRule>()
                    .WithMany()
                    .HasForeignKey(a => a.RuleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<VatRule>(entity =>
            {
                entity.Property(r => r.Rate)
                    .HasColumnType("decimal(18,4)");
                entity.HasIndex(r => new { r.Country, r.EffectiveFrom });
            });

            builder.Entity<VatRuleAudit>(entity =>
            {
                entity.Property(a => a.Action)
                    .HasDefaultValue("Updated")
                    .HasMaxLength(32);
                entity.HasIndex(a => a.RuleId);
                entity.HasOne<VatRule>()
                    .WithMany()
                    .HasForeignKey(a => a.RuleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<CurrencySetting>(entity =>
            {
                entity.Property(c => c.Code)
                    .IsRequired()
                    .HasMaxLength(16);
                entity.Property(c => c.Name)
                    .HasMaxLength(128);
                entity.Property(c => c.RateSource)
                    .HasMaxLength(128);
                entity.Property(c => c.LatestRate)
                    .HasColumnType("decimal(18,6)");
                entity.HasIndex(c => c.Code)
                    .IsUnique();
            });

            builder.Entity<IntegrationConfiguration>(entity =>
            {
                entity.Property(i => i.Key)
                    .IsRequired()
                    .HasMaxLength(64);
                entity.Property(i => i.Name)
                    .IsRequired()
                    .HasMaxLength(128);
                entity.Property(i => i.Type)
                    .IsRequired()
                    .HasMaxLength(32);
                entity.Property(i => i.Environment)
                    .IsRequired()
                    .HasMaxLength(32);
                entity.Property(i => i.ApiKey)
                    .HasMaxLength(256);
                entity.Property(i => i.Endpoint)
                    .HasMaxLength(256);
                entity.Property(i => i.MerchantId)
                    .HasMaxLength(128);
                entity.Property(i => i.CallbackUrl)
                    .HasMaxLength(256);
                entity.Property(i => i.Status)
                    .HasMaxLength(32)
                    .HasDefaultValue(IntegrationStatuses.Configured);
                entity.Property(i => i.LastHealthCheckMessage)
                    .HasMaxLength(512);
                entity.Property(i => i.Enabled)
                    .HasDefaultValue(true);
                entity.HasIndex(i => new { i.Key, i.Environment })
                    .IsUnique();
            });

            builder.Entity<FeatureFlag>(entity =>
            {
                entity.Property(f => f.Key)
                    .IsRequired()
                    .HasMaxLength(128);
                entity.Property(f => f.Name)
                    .IsRequired()
                    .HasMaxLength(256);
                entity.Property(f => f.Description)
                    .HasMaxLength(512);
                entity.Property(f => f.CreatedBy)
                    .HasMaxLength(450);
                entity.Property(f => f.CreatedByName)
                    .HasMaxLength(256);
                entity.Property(f => f.UpdatedBy)
                    .HasMaxLength(450);
                entity.Property(f => f.UpdatedByName)
                    .HasMaxLength(256);
                entity.HasIndex(f => f.Key)
                    .IsUnique();
            });

            builder.Entity<FeatureFlagEnvironment>(entity =>
            {
                entity.Property(e => e.Environment)
                    .IsRequired()
                    .HasMaxLength(32);
                entity.Property(e => e.TargetingJson)
                    .HasColumnType("nvarchar(max)");
                entity.HasIndex(e => new { e.FlagId, e.Environment })
                    .IsUnique();
                entity.HasOne(e => e.Flag)
                    .WithMany(f => f.Environments)
                    .HasForeignKey(e => e.FlagId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<FeatureFlagAudit>(entity =>
            {
                entity.Property(a => a.Action)
                    .HasMaxLength(32)
                    .HasDefaultValue("Updated");
                entity.Property(a => a.ActorId)
                    .HasMaxLength(450);
                entity.Property(a => a.ActorName)
                    .HasMaxLength(256);
                entity.HasIndex(a => a.FlagId);
                entity.HasOne<FeatureFlag>()
                    .WithMany()
                    .HasForeignKey(a => a.FlagId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ProcessingActivity>(entity =>
            {
                entity.Property(a => a.Name)
                    .IsRequired()
                    .HasMaxLength(256);
                entity.Property(a => a.Purpose)
                    .IsRequired()
                    .HasMaxLength(1024);
                entity.Property(a => a.LegalBasis)
                    .IsRequired()
                    .HasMaxLength(512);
                entity.Property(a => a.DataCategories)
                    .IsRequired()
                    .HasMaxLength(1024);
                entity.Property(a => a.DataSubjects)
                    .IsRequired()
                    .HasMaxLength(512);
                entity.Property(a => a.Processors)
                    .IsRequired()
                    .HasMaxLength(1024);
                entity.Property(a => a.RetentionPeriod)
                    .IsRequired()
                    .HasMaxLength(256);
                entity.Property(a => a.DataTransfers)
                    .HasMaxLength(512);
                entity.Property(a => a.SecurityMeasures)
                    .HasMaxLength(1024);
                entity.Property(a => a.CreatedById)
                    .HasMaxLength(450);
                entity.Property(a => a.CreatedByName)
                    .HasMaxLength(256);
                entity.Property(a => a.UpdatedById)
                    .HasMaxLength(450);
                entity.Property(a => a.UpdatedByName)
                    .HasMaxLength(256);
                entity.HasIndex(a => a.Name)
                    .IsUnique();
            });

            builder.Entity<ProcessingActivityRevision>(entity =>
            {
                entity.Property(r => r.ChangeType)
                    .HasMaxLength(32)
                    .HasDefaultValue("Updated");
                entity.Property(r => r.ChangedFields)
                    .HasMaxLength(512);
                entity.Property(r => r.ChangedById)
                    .HasMaxLength(450);
                entity.Property(r => r.ChangedByName)
                    .HasMaxLength(256);
                entity.Property(r => r.SnapshotJson)
                    .HasColumnType("nvarchar(max)");
                entity.HasIndex(r => r.ProcessingActivityId);
                entity.HasOne(r => r.ProcessingActivity)
                    .WithMany(a => a.Revisions)
                    .HasForeignKey(r => r.ProcessingActivityId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ConsentDefinition>(entity =>
            {
                entity.Property(d => d.ConsentType)
                    .IsRequired()
                    .HasMaxLength(64);
                entity.Property(d => d.Title)
                    .IsRequired()
                    .HasMaxLength(128);
                entity.Property(d => d.Description)
                    .HasMaxLength(512);
                entity.HasIndex(d => d.ConsentType)
                    .IsUnique();
            });

            builder.Entity<ConsentVersion>(entity =>
            {
                entity.Property(v => v.VersionTag)
                    .IsRequired()
                    .HasMaxLength(64);
                entity.Property(v => v.Content)
                    .IsRequired()
                    .HasColumnType("nvarchar(max)");
                entity.HasIndex(v => new { v.ConsentDefinitionId, v.VersionTag })
                    .IsUnique();
                entity.HasOne(v => v.ConsentDefinition)
                    .WithMany(d => d.Versions)
                    .HasForeignKey(v => v.ConsentDefinitionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<UserConsentDecision>(entity =>
            {
                entity.Property(c => c.UserId)
                    .IsRequired()
                    .HasMaxLength(450);
                entity.Property(c => c.DecidedOn)
                    .IsRequired();
                entity.HasIndex(c => new { c.UserId, c.DecidedOn });
                entity.HasIndex(c => new { c.UserId, c.ConsentVersionId });
                entity.HasOne(c => c.ConsentVersion)
                    .WithMany()
                    .HasForeignKey(c => c.ConsentVersionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<LegalDocumentVersion>(entity =>
            {
                entity.Property(d => d.DocumentType)
                    .IsRequired()
                    .HasMaxLength(64);
                entity.Property(d => d.VersionTag)
                    .IsRequired()
                    .HasMaxLength(64);
                entity.Property(d => d.Title)
                    .HasMaxLength(256);
                entity.Property(d => d.CreatedBy)
                    .HasMaxLength(450);
                entity.Property(d => d.CreatedByName)
                    .HasMaxLength(256);
                entity.Property(d => d.UpdatedBy)
                    .HasMaxLength(450);
                entity.Property(d => d.UpdatedByName)
                    .HasMaxLength(256);
                entity.HasIndex(d => new { d.DocumentType, d.EffectiveFrom });
                entity.HasIndex(d => new { d.DocumentType, d.VersionTag })
                    .IsUnique();
            });
        }
    }
}
