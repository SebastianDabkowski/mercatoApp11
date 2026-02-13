using System;
using System.Text.RegularExpressions;

namespace SD.ProjectName.Tests.Products
{
    public class DocumentationConsistencyTests
    {
        [Fact]
        public void ArchitectureDocument_ShouldListAllAdrFiles()
        {
            var repoRoot = FindRepoRoot();
            var architectureDoc = File.ReadAllText(Path.Combine(repoRoot, "ARCHITECTURE.md"));
            var adrFiles = Directory.GetFiles(Path.Combine(repoRoot, "docs", "adr"), "adr-*.md");

            Assert.NotEmpty(adrFiles);

            foreach (var adrPath in adrFiles)
            {
                var match = Regex.Match(Path.GetFileNameWithoutExtension(adrPath), @"adr-(\d{3})", RegexOptions.IgnoreCase);
                Assert.True(match.Success, $"ADR filename '{adrPath}' must include a three-digit identifier.");

                var adrId = $"ADR-{match.Groups[1].Value}";
                Assert.Contains(adrId, architectureDoc);
            }
        }

        [Fact]
        public void ArchitectureDocument_ShouldMentionAllModules()
        {
            var repoRoot = FindRepoRoot();
            var architectureDoc = File.ReadAllText(Path.Combine(repoRoot, "ARCHITECTURE.md"));
            var modulesRoot = Path.Combine(repoRoot, "src", "Modules");

            if (!Directory.Exists(modulesRoot))
            {
                return;
            }

            var moduleFolders = Directory.GetDirectories(modulesRoot)
                .Select(path => Path.GetFileName(path) ?? string.Empty)
                .Where(name => name.Length > 0)
                .ToArray();

            foreach (var module in moduleFolders)
            {
                Assert.Contains(module, architectureDoc);
            }
        }

        [Fact]
        public void AgentDocument_ShouldReinforceDocSyncResponsibilities()
        {
            var repoRoot = FindRepoRoot();
            var agentDoc = File.ReadAllText(Path.Combine(repoRoot, "AGENT.md"));

            Assert.Contains("Auto-update Architecture.md", agentDoc);
            Assert.Contains("Auto-update AGENT.md", agentDoc);
            Assert.Contains("ADR validation", agentDoc, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Documentation_ShouldDescribeAutomationAndAdrRemediation()
        {
            var repoRoot = FindRepoRoot();
            var architectureDoc = File.ReadAllText(Path.Combine(repoRoot, "ARCHITECTURE.md"));
            var agentDoc = File.ReadAllText(Path.Combine(repoRoot, "AGENT.md"));

            Assert.Contains("doc-sync automation", agentDoc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("validate ADRs against the current architecture", agentDoc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("doc-sync automation", architectureDoc, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("missing ADR", architectureDoc, StringComparison.OrdinalIgnoreCase);
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ARCHITECTURE.md")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}
