using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArpsForecasting
{
    public static class PdfReportGenerator
    {
        public static string GenerateChartDeck(string chartsDir, string reportTitle = "Forecast Charts")
        {
            if (string.IsNullOrWhiteSpace(chartsDir))
                throw new ArgumentException("Charts directory is required.", nameof(chartsDir));

            var images = Directory.GetFiles(chartsDir, "*_forecast.png")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (images.Count == 0)
                throw new InvalidOperationException($"No chart images found in '{chartsDir}'.");

            var outputPath = Path.Combine(chartsDir, "forecast-charts.pdf");
            QuestPDF.Settings.License = LicenseType.Community;

            Document.Create(container =>
            {
                foreach (var image in images)
                {
                    var name = Path.GetFileNameWithoutExtension(image);
                    container.Page(page =>
                    {
                        page.Size(PageSizes.Letter.Landscape());
                        page.Margin(20);
                        page.DefaultTextStyle(x => x.FontSize(12));

                        page.Header()
                            .Row(row =>
                            {
                                row.RelativeItem().Text(reportTitle).SemiBold();
                                row.RelativeItem().AlignRight().Text(name);
                            });

                        page.Content()
                            .PaddingTop(10)
                            .Image(image)
                            .FitArea();

                        page.Footer()
                            .AlignCenter()
                            .Text(x =>
                            {
                                x.Span("Generated ");
                                x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                            });
                    });
                }
            }).GeneratePdf(outputPath);

            return outputPath;
        }
    }
}
