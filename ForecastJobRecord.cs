using System;
using System.Collections.Generic;

namespace ArpsForecasting
{
    public enum ForecastJobStatus
    {
        Uploaded = 0,
        Running = 1,
        Completed = 2,
        Failed = 3
    }

    public class ForecastJobRecord
    {
        public Guid JobId { get; set; }
        public ForecastJobStatus Status { get; set; } = ForecastJobStatus.Uploaded;
        public string SubmissionKind { get; set; } = "upload";
        public string OriginalFileName { get; set; } = string.Empty;
        public string SourceCsvPath { get; set; } = string.Empty;
        public string StoredCsvPath { get; set; } = string.Empty;
        public string ChartsOutputDir { get; set; } = string.Empty;
        public string JobRootPath { get; set; } = string.Empty;
        public int PhaseMode { get; set; } = 4;
        public bool GeneratePdf { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public List<string> Messages { get; set; } = new();
        public List<ForecastJobArtifact> Artifacts { get; set; } = new();
        public ForecastRunResult? Result { get; set; }
    }

    public class LocalForecastJobRequest
    {
        public string CsvPath { get; set; } = string.Empty;
        public int PhaseMode { get; set; } = 4;
        public bool GeneratePdf { get; set; }
    }

    public class ForecastJobArtifact
    {
        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
