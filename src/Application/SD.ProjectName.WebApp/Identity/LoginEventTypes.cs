namespace SD.ProjectName.WebApp.Identity
{
    public static class LoginEventTypes
    {
        public const string PasswordSuccess = "PasswordSuccess";
        public const string PasswordFailed = "PasswordFailed";
        public const string LockedOut = "LockedOut";
        public const string RequiresTwoFactor = "RequiresTwoFactor";
        public const string TwoFactorSuccess = "TwoFactorSuccess";
        public const string TwoFactorFailed = "TwoFactorFailed";
        public const string BlockedUnverified = "BlockedUnverified";
        public const string UnknownUser = "UnknownUser";
    }
}
