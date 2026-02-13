namespace SD.ProjectName.Modules.Products.Domain
{
    public static class ProductWorkflowStates
    {
        public const string Draft = "draft";
        public const string Active = "active";
        public const string Suspended = "suspended";
        public const string Archived = "archived";

        public static bool IsValid(string state) =>
            state == Draft ||
            state == Active ||
            state == Suspended ||
            state == Archived;
    }
}
