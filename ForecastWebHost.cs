using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace ArpsForecasting
{
    public static class ForecastWebHost
    {
        public static int Run(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSingleton<ForecastJobService>();
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapGet("/api/health", () => Results.Ok(new
            {
                ok = true,
                service = "TaurisAI-Forecasting",
                utc = DateTime.UtcNow
            }));

            app.MapGet("/api/forecast-jobs", (ForecastJobService jobService) =>
                Results.Ok(jobService.List()));

            app.MapGet("/api/local-csvs", (ForecastJobService jobService) =>
                Results.Ok(jobService.ListKnownLocalCsvPaths()));

            app.MapGet("/api/forecast-jobs/{jobId:guid}", (Guid jobId, ForecastJobService jobService) =>
            {
                var job = jobService.Get(jobId);
                return job is null ? Results.NotFound() : Results.Ok(job);
            });

            app.MapPost("/api/forecast-jobs", async (HttpRequest request, ForecastJobService jobService, CancellationToken cancellationToken) =>
            {
                if (!request.HasFormContentType)
                    return Results.BadRequest(new { error = "Expected multipart/form-data upload." });

                var form = await request.ReadFormAsync(cancellationToken);
                var file = form.Files["file"];
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { error = "A CSV file is required." });

                var phaseMode = ParsePhaseMode(form["phaseMode"].FirstOrDefault());
                var generatePdf = ParseBool(form["generatePdf"].FirstOrDefault(), fallback: true);
                var job = await jobService.CreateAsync(file, phaseMode, generatePdf, cancellationToken);
                return Results.Accepted($"/api/forecast-jobs/{job.JobId}", job);
            });

            app.MapPost("/api/forecast-jobs/local", async (LocalForecastJobRequest payload, ForecastJobService jobService, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(payload.CsvPath))
                    return Results.BadRequest(new { error = "A local CSV path is required." });

                try
                {
                    var job = await jobService.CreateFromLocalPathAsync(payload.CsvPath, ParsePhaseMode(payload.PhaseMode), payload.GeneratePdf, cancellationToken);
                    return Results.Accepted($"/api/forecast-jobs/{job.JobId}", job);
                }
                catch (FileNotFoundException)
                {
                    return Results.BadRequest(new { error = "The specified local CSV path was not found." });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            app.MapGet("/api/forecast-jobs/{jobId:guid}/artifacts/{*relativePath}", (Guid jobId, string relativePath, ForecastJobService jobService) =>
            {
                var path = jobService.ResolveArtifactPath(jobId, relativePath);
                if (string.IsNullOrWhiteSpace(path))
                    return Results.NotFound();

                return Results.File(path, contentType: null, fileDownloadName: System.IO.Path.GetFileName(path));
            });

            Console.WriteLine("TaurisAI-Forecasting web harness");
            Console.WriteLine("Open http://localhost:5000 or the ASP.NET Core URL shown below.");
            app.Run();
            return 0;
        }

        private static int ParsePhaseMode(string? value)
        {
            return int.TryParse(value, out var parsed) && (parsed == 0 || parsed == 3 || parsed == 4)
                ? parsed
                : 4;
        }

        private static int ParsePhaseMode(int value)
        {
            return value is 0 or 3 or 4 ? value : 4;
        }

        private static bool ParseBool(string? value, bool fallback)
        {
            return bool.TryParse(value, out var parsed) ? parsed : fallback;
        }
    }
}
