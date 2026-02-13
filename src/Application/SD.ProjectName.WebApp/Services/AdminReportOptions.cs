namespace SD.ProjectName.WebApp.Services
{
    public class AdminReportOptions
    {
        public const string SectionName = "AdminReports";

        public int ExportRowLimit { get; set; } = 20000;

        public int PreviewPageSize { get; set; } = 50;
    }
}
