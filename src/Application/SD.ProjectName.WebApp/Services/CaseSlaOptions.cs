using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SD.ProjectName.WebApp.Services
{
    public class CaseSlaOptions
    {
        public const string SectionName = "CaseSla";

        public bool Enabled { get; set; } = true;

        [Range(1, 720)]
        public int DefaultFirstResponseHours { get; set; } = 24;

        [Range(1, 720)]
        public int DefaultResolutionHours { get; set; } = 120;

        public Dictionary<string, CaseSlaRule> CategoryRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class CaseSlaRule
    {
        [Range(1, 720)]
        public int? FirstResponseHours { get; set; }

        [Range(1, 720)]
        public int? ResolutionHours { get; set; }
    }
}
