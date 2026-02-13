namespace SD.ProjectName.WebApp.Services;

public class PushNotificationOptions
{
    public const string SectionName = "PushNotifications";

    public bool Enabled { get; set; } = true;

    public string? Subject { get; set; } = "mailto:support@mercato.test";

    public string? PublicKey { get; set; }

    public string? PrivateKey { get; set; }
}
