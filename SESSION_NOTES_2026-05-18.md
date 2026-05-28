# TaurisAI-Forecasting Session Notes (May 18, 2026)

## Scope Completed
- Added first-pass multi-stream batch forecasting support for `Oil`, `Gas`, `Water`, and optional pressure fields.
- Added well-level parallel forecasting across a single uploaded CSV containing many wells.
- Added flat parameter export artifacts intended for downstream storage/integration.
- Added first derived ratio export path for `WOR = Water / Oil`.
- Reworked the local browser harness into a review-first UI:
  - upload and start immediately
  - auto-open latest completed run
  - well rail with `green / yellow / red`
  - VCR navigation
  - keyboard shortcuts
  - per-well Cum./EUR cards
  - manual review state saved in browser storage

## Key Code Changes

### 1) Multi-stream ingestion and batch orchestration
- File: `Program.cs`
- `RunForecast(...)` now:
  - loads legacy single-series CSVs and multi-stream CSVs
  - processes wells in parallel with `Parallel.ForEach`
  - writes:
    - `arps_parameters.csv`
    - `arps_aries_phdwin.csv`
    - `forecast_parameters.csv`
    - `forecast_parameters.json`
    - `forecast_run_summary.json`
- `LoadWellData(...)` now accepts:
  - `Production` / `Rate`
  - `Oil` / `OilRate` / `GrossOil`
  - `Gas` / `GasRate` / `GrossGas`
  - `Water` / `WaterRate` / `GrossWater`
  - optional `Pressure` / `TubingPressure`

### 2) New flat export / ratio types
- Files:
  - `ForecastParameterRow.cs`
  - `RatioDefinition.cs`
  - `WellForecastResult.cs`
- Added:
  - flat export DTO for ARPS and ratio rows
  - default ratio definition for `WOR`
  - per-well forecast result wrapper

### 3) Well model expansion
- File: `WellSeries.cs`
- `WellSeries` now carries:
  - legacy `Production`
  - `PhaseSeries` dictionary for multi-stream histories
  - helper methods to query available phase series

### 4) Chart generator reuse by metric
- File: `WellChartGenerator.cs`
- Added:
  - `resultWellName`
  - `outputFileStem`
  - `yAxisLabel`
- This allows one well to emit separate chart files for `Oil`, `Gas`, and legacy `Production`.

### 5) Run result expansion
- File: `ForecastRunResult.cs`
- Added:
  - `ForecastParametersPath`
  - `ForecastParametersJsonPath`
  - `ForecastParameterCount`

### 6) Harness update
- File: `SampleHarness.cs`
- Harness now also validates:
  - `forecast_parameters.csv`
  - `forecast_parameters.json`

### 7) Review-first local UI
- Files:
  - `wwwroot/index.html`
  - `wwwroot/app.js`
  - `wwwroot/styles.css`
- New UI behavior:
  - file selection kicks off processing immediately
  - latest completed job auto-loads into review workspace
  - side rail of wells with QC status
  - phase tabs and chart viewer
  - per-well metric cards for Oil/Gas/Water Cum. and EUR
  - 2-stream EUR display
  - lateral length display when present in source CSV
  - VCR navigation buttons
  - keyboard shortcuts:
    - `,` and `.` navigate wells
    - `1`, `2`, `3` set QC
    - `[` and `]` shift fit start
    - `-` and `=` adjust `Di`
    - `B` and `N` adjust `b`
    - `S` saves review
- Review state is currently browser-local only and does not rerun the backend forecast.

### 8) Runtime README refresh
- File: `README.md`
- Updated the high-signal sections to document:
  - current runtime behavior
  - new multi-stream support
  - new flat export artifacts

## What Was Verified
- `dotnet build`
- `node --check wwwroot/app.js`
- Multi-stream smoke run using a synthetic CSV with:
  - `Oil`
  - `Gas`
  - `Water`
  - `Pressure`
- Smoke run confirmed:
  - separate `Oil` and `Gas` chart generation
  - `forecast_parameters.csv`
  - `forecast_parameters.json`
  - derived `WOR` row
- Local web host started successfully on:
  - `http://127.0.0.1:5000`
- Health endpoint verified:
  - `GET /api/health`

## Current Runtime State
- Local server was last started with:
```bash
dotnet run -- --web --urls http://127.0.0.1:5000
```
- Latest known good URL:
  - `http://127.0.0.1:5000`
- Health response was successful during this session.

## Known Gaps / Next Technical Steps

### High priority
1. Add true batch mode:
   - disable chart/PDF generation by default for large uploads
   - make flat parameter export the primary artifact
2. Upgrade CSV parsing:
   - current parser still uses basic comma splitting in backend
   - replace with a real CSV parser before relying on field-sized datasets
3. Persist manual review state server-side:
   - current UI review edits are browser-local only
   - need API contract + storage + rerun hook
4. Add review-save-to-rerun workflow:
   - UI fit shifts and QC flags should be able to trigger a backend recalculation

### Forecasting/product next steps
5. Extend ratio layer:
   - `GOR`
   - NGL yield definitions
   - configurable formula definitions instead of a single hardcoded default
6. Reconstruct dependent volume forecasts:
   - use `WOR * Oil forecast` for Water time-series
   - later use yield * anchor-stream forecast for NGL
7. Add well-level persisted QC / diagnostics:
   - selected model
   - why selected
   - backtest metrics
   - review notes
8. Add large-run operational behavior:
   - progress reporting
   - throttled concurrency
   - failed-well retry / exception queue

## Product / UX Notes Captured
- Desired workflow is not “upload many files and wait to click process.”
- Desired workflow is:
  - upload one CSV containing many wells
  - start processing immediately
  - when complete, scroll and review wells quickly
- Desired review controls include:
  - `green / yellow / red`
  - VCR navigation
  - shortcut keys
  - manual fit adjustments
  - save review state
- Desired well context includes:
  - Cum. and EUR for Oil / Gas / Water
  - 2-stream EUR
  - lateral length when available

## Suggested Next Session Entry Points
1. Add backend review-save endpoints and persisted review model.
2. Add batch-mode run option with charts off by default.
3. Replace naive backend CSV parsing with a robust parser.
4. Add dependent water volume forecast reconstruction from `WOR`.
5. Add explicit lateral-length ingestion to export/output contracts.
