using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ArpsForecasting
{
    public sealed class ForecastJobService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ConcurrentDictionary<Guid, ForecastJobRecord> _jobs = new();
        private readonly string _jobsRootPath;

        public ForecastJobService()
        {
            _jobsRootPath = Path.Combine(Directory.GetCurrentDirectory(), "runtime", "forecast-jobs");
            Directory.CreateDirectory(_jobsRootPath);
            LoadExistingJobs();
        }

        public IReadOnlyList<ForecastJobRecord> List()
        {
            return _jobs.Values
                .OrderByDescending(job => job.CreatedAtUtc)
                .Select(CloneJob)
                .ToList();
        }

        public ForecastJobRecord? Get(Guid jobId)
        {
            return _jobs.TryGetValue(jobId, out var job) ? CloneJob(job) : null;
        }

        public async Task<ForecastJobRecord> CreateAsync(
            IFormFile file,
            int phaseMode,
            bool generatePdf,
            CancellationToken cancellationToken)
        {
            var originalName = string.IsNullOrWhiteSpace(file.FileName) ? "production_data.csv" : file.FileName;
            return await CreateJobAsync(
                submissionKind: "upload",
                originalFileName: originalName,
                sourceCsvPath: originalName,
                safeFileName: SanitizeFileName(originalName),
                phaseMode: phaseMode,
                generatePdf: generatePdf,
                async destinationPath =>
                {
                    await using var stream = File.Create(destinationPath);
                    await file.CopyToAsync(stream, cancellationToken);
                });
        }

        public async Task<ForecastJobRecord> CreateFromLocalPathAsync(
            string csvPath,
            int phaseMode,
            bool generatePdf,
            CancellationToken cancellationToken)
        {
            var fullPath = ResolveLocalCsvPath(csvPath);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                throw new FileNotFoundException("Local CSV path was not found.", csvPath);

            if (!fullPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only CSV inputs are supported for local path jobs.");

            return await CreateJobAsync(
                submissionKind: "local-path",
                originalFileName: Path.GetFileName(fullPath),
                sourceCsvPath: fullPath,
                safeFileName: SanitizeFileName(Path.GetFileName(fullPath)),
                phaseMode: phaseMode,
                generatePdf: generatePdf,
                async destinationPath =>
                {
                    await using var source = File.OpenRead(fullPath);
                    await using var destination = File.Create(destinationPath);
                    await source.CopyToAsync(destination, cancellationToken);
                });
        }

        public IReadOnlyList<string> ListKnownLocalCsvPaths()
        {
            var roots = new[]
            {
                Directory.GetCurrentDirectory(),
                Path.Combine(Directory.GetCurrentDirectory(), "harness", "input")
            }
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            var ignoredSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bin",
                "obj",
                "runtime",
                ".git"
            };

            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                foreach (var path in EnumerateCsvFiles(root, maxDepth: 3, ignoredSegments))
                {
                    if (!LooksLikeForecastInputCsv(path))
                        continue;

                    results.Add(path);
                    if (results.Count >= 100)
                        return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }

            return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private async Task<ForecastJobRecord> CreateJobAsync(
            string submissionKind,
            string originalFileName,
            string sourceCsvPath,
            string safeFileName,
            int phaseMode,
            bool generatePdf,
            Func<string, Task> writeInputAsync)
        {
            var jobId = Guid.NewGuid();
            var jobRootPath = GetJobRootPath(jobId);
            var inputDir = Path.Combine(jobRootPath, "input");
            var outputDir = Path.Combine(jobRootPath, "output");
            var chartsDir = Path.Combine(outputDir, "Charts");
            var metadataDir = Path.Combine(jobRootPath, "metadata");

            Directory.CreateDirectory(inputDir);
            Directory.CreateDirectory(outputDir);
            Directory.CreateDirectory(chartsDir);
            Directory.CreateDirectory(metadataDir);

            var storedCsvPath = Path.Combine(inputDir, safeFileName);
            await writeInputAsync(storedCsvPath);

            var job = new ForecastJobRecord
            {
                JobId = jobId,
                Status = ForecastJobStatus.Uploaded,
                SubmissionKind = submissionKind,
                OriginalFileName = originalFileName,
                SourceCsvPath = sourceCsvPath,
                StoredCsvPath = storedCsvPath,
                ChartsOutputDir = chartsDir,
                JobRootPath = jobRootPath,
                PhaseMode = phaseMode,
                GeneratePdf = generatePdf,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Messages = new List<string> { "Upload saved and forecast job queued." }
            };

            _jobs[jobId] = job;
            SaveJob(job);
            _ = Task.Run(() => RunJobAsync(jobId));
            return CloneJob(job);
        }

        public string? ResolveArtifactPath(Guid jobId, string relativePath)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
                return null;

            var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(job.JobRootPath, normalized));
            if (!fullPath.StartsWith(job.JobRootPath, StringComparison.OrdinalIgnoreCase))
                return null;

            return File.Exists(fullPath) ? fullPath : null;
        }

        private void LoadExistingJobs()
        {
            foreach (var metadataPath in Directory.EnumerateFiles(_jobsRootPath, "job.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(metadataPath);
                    var job = JsonSerializer.Deserialize<ForecastJobRecord>(json);
                    if (job is null || job.JobId == Guid.Empty)
                        continue;

                    RefreshArtifacts(job);
                    _jobs[job.JobId] = job;
                }
                catch
                {
                    // Ignore malformed local metadata and continue loading valid jobs.
                }
            }
        }

        private async Task RunJobAsync(Guid jobId)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
                return;

            UpdateJob(job, ForecastJobStatus.Running, "Forecast run started.");
            try
            {
                var result = Program.RunForecast(job.StoredCsvPath, job.ChartsOutputDir, job.GeneratePdf, job.PhaseMode);
                job.Result = result;
                job.Messages.AddRange(result.Messages);
                RefreshArtifacts(job);
                UpdateJob(job, result.Success ? ForecastJobStatus.Completed : ForecastJobStatus.Failed,
                    result.Success ? "Forecast run completed." : "Forecast run finished with validation failures.");
            }
            catch (Exception ex)
            {
                job.Messages.Add($"Unhandled forecast job failure: {ex.Message}");
                RefreshArtifacts(job);
                UpdateJob(job, ForecastJobStatus.Failed, "Forecast run failed.");
            }

            await Task.CompletedTask;
        }

        private void UpdateJob(ForecastJobRecord job, ForecastJobStatus status, string message)
        {
            lock (job)
            {
                job.Status = status;
                job.UpdatedAtUtc = DateTime.UtcNow;
                job.Messages.Add(message);
                SaveJob(job);
            }
        }

        private void RefreshArtifacts(ForecastJobRecord job)
        {
            var artifacts = new List<ForecastJobArtifact>();
            if (Directory.Exists(job.JobRootPath))
            {
                foreach (var path in Directory.EnumerateFiles(job.JobRootPath, "*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var relativePath = Path.GetRelativePath(job.JobRootPath, path).Replace('\\', '/');
                    if (relativePath.StartsWith("metadata/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    artifacts.Add(new ForecastJobArtifact
                    {
                        Name = Path.GetFileName(path),
                        RelativePath = relativePath,
                        ContentType = GetContentType(path),
                        DownloadUrl = $"/api/forecast-jobs/{job.JobId}/artifacts/{relativePath}"
                    });
                }
            }

            lock (job)
            {
                job.Artifacts = artifacts;
                SaveJob(job);
            }
        }

        private void SaveJob(ForecastJobRecord job)
        {
            var metadataDir = Path.Combine(job.JobRootPath, "metadata");
            Directory.CreateDirectory(metadataDir);
            var metadataPath = Path.Combine(metadataDir, "job.json");
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(job, SerializerOptions));
        }

        private string GetJobRootPath(Guid jobId)
        {
            return Path.Combine(_jobsRootPath, jobId.ToString("N"));
        }

        private static ForecastJobRecord CloneJob(ForecastJobRecord job)
        {
            return JsonSerializer.Deserialize<ForecastJobRecord>(
                       JsonSerializer.Serialize(job, SerializerOptions),
                       SerializerOptions)
                   ?? new ForecastJobRecord();
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                return "production_data.csv";
            if (!cleaned.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                cleaned += ".csv";
            return cleaned;
        }

        private static string? ResolveLocalCsvPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
        }

        private static IEnumerable<string> EnumerateCsvFiles(string root, int maxDepth, ISet<string> ignoredSegments)
        {
            return EnumerateCsvFilesCore(root, currentDepth: 0, maxDepth, ignoredSegments);
        }

        private static IEnumerable<string> EnumerateCsvFilesCore(string root, int currentDepth, int maxDepth, ISet<string> ignoredSegments)
        {
            IEnumerable<string> files = Enumerable.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(root, "*.csv", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                yield break;
            }

            foreach (var file in files)
                yield return file;

            if (currentDepth >= maxDepth)
                yield break;

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => !ignoredSegments.Contains(Path.GetFileName(path)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                yield break;
            }

            foreach (var directory in directories)
            {
                foreach (var file in EnumerateCsvFilesCore(directory, currentDepth + 1, maxDepth, ignoredSegments))
                    yield return file;
            }
        }

        private static bool LooksLikeForecastInputCsv(string path)
        {
            try
            {
                using var reader = new StreamReader(path);
                var header = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(header))
                    return false;

                var columns = header.Split(',')
                    .Select(value => value.Trim().Replace(" ", string.Empty).ToLowerInvariant())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                return columns.Contains("wellname")
                    && columns.Contains("time")
                    && columns.Contains("production");
            }
            catch
            {
                return false;
            }
        }

        private static string GetContentType(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.ToLowerInvariant() switch
            {
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                _ => "application/octet-stream"
            };
        }
    }
}
