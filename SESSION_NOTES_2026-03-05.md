# ArpsForecasting Session Notes (March 5, 2026)

## Scope Completed
- Upgraded forecasting workflow to improve trend-fit quality and operational reviewability.
- Added cadence-aware handling (daily/monthly), optional pressure-aware features, and stronger fitting/validation logic.
- Added chart deck PDF generation.
- Temporarily disabled per-well series CSV export per request.

## Key Code Changes

### 1) Data ingestion and quality
- File: `Program.cs`
- Replaced hardcoded file paths with runtime args/defaults.
- Header-based CSV parsing:
  - Required: `WellName`/`Well`/`Name`, `Time`/`Day`/`Month`, `Production`/`Rate`/`OilRate`
  - Optional: `Pressure`/`ReservoirPressure`/`TubingPressure`
- Added well-level cleaning:
  - drop invalid rows
  - dedupe by time
  - sort by time
  - outlier clipping (MAD-based)
- Added cadence detection (daily vs monthly) and metadata packaging via `WellSeries`.

### 2) New cadence model object
- File: `WellSeries.cs`
- Added:
  - `CadenceType` enum (`Daily`, `Monthly`)
  - `WellSeries` class with `Time`, `Production`, `Pressure`, `Cadence`, `StepDays`

### 3) Hybrid model improvements
- File: `HybridForecaster.cs`
- Added pressure-aware and trend/state features:
  - pressure, pressure delta
  - rate delta
  - moving average
  - cumulative production proxy
- Constructor and forecast now accept optional pressure series.

### 4) ARPS fitting robustness
- File: `SSE.cs`
- Updated hyperbolic fitting objective to weighted relative error (less bias from high-rate points).
- (Earlier in session) ARPS fitting moved away from placeholder behavior.

### 5) Forecast orchestration / evaluation
- File: `WellChartGenerator.cs`
- Reworked generation pipeline:
  - cadence-aware config scaling
  - simple last change-point detection for decline segment focus
  - pre-drill ARPS baseline forecast
  - forecast constraints in future horizon (avoid unrealistic upward drift)
  - uncertainty bands (P10/P90) from residual ratio quantiles
  - rolling-origin backtest scoring + tail-window scoring
  - champion model selection by rolling error
- Console output now includes per-model tail + rolling metrics and pre-drill hold-up metrics.

### 6) Reporting artifacts
- File: `PdfReportGenerator.cs`
- Generates `forecast-charts.pdf` from all `*_forecast.png` in output directory.

### 7) Runtime dependency for WSL/Linux plotting
- File: `ArpsForecasting.csproj`
- Added:
  - `SkiaSharp.NativeAssets.Linux` for ScottPlot native runtime support in WSL/Linux.
  - `QuestPDF` for PDF deck generation.

## Outputs Produced (latest run)
- Charts: `Charts/*_forecast.png`
- PDF deck: `Charts/forecast-charts.pdf`
- Parameters: `arps_parameters.csv`
- Note: `*_series.csv` generation is intentionally disabled currently.

## Run Command
```bash
cd /mnt/c/Dev/OilGas/ArpsForecasting
dotnet run -- "/mnt/c/Dev/OilGas/ArpsForecasting/production_data.csv" "/mnt/c/Dev/OilGas/ArpsForecasting/Charts"
```

## Current Status / Known Notes
- Pipeline runs end-to-end in WSL with native plotting now working.
- If PDF is open in another app, PDF write can fail due to file lock.
- One compile warning remains in `Program.cs` related to nullable pressure handling; non-blocking for run.

## Deferred by request
- Per-well series CSV export (`HIST-` / `FOR-`) is currently disabled.
- Re-enable later in a separate prompt if needed.

## Suggested Next Session Entry Points
1. Tune change-point sensitivity and rolling window lengths by basin/well type.
2. Add explicit daily-vs-monthly backtest report summary table (per well champion + WAPE/sMAPE).
3. Re-enable/export series CSV if stakeholder review workflow needs raw series audit.
