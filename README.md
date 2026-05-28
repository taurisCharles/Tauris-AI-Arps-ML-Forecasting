# TaurisAI-Forecasting

## Planning Docs

- [SERVICE_CONTRACT.md](./SERVICE_CONTRACT.md)
- [PRODUCT_REQUIREMENTS.md](./PRODUCT_REQUIREMENTS.md)
- [CHART_CONTROL_SPEC.md](./CHART_CONTROL_SPEC.md)
- [ARCHITECTURE.md](./ARCHITECTURE.md)
- [SESSION_NOTES_2026-03-05.md](./SESSION_NOTES_2026-03-05.md)

## Current Codebase Note

This repository currently contains the earlier `ArpsForecasting` prototype codebase, which is being evolved into the broader `TaurisAI-Forecasting` web application and forecasting engine.

The planning docs remain useful for direction, but the live runtime has moved beyond parts of the legacy notes below. Treat the executable code paths in `Program.cs`, `ForecastJobService.cs`, `WellChartGenerator.cs`, and the related DTOs as the current source of truth.

## Current Runtime Behavior

- Input is CSV-based.
- The loader accepts:
  - legacy single-series files with `Production` / `Rate`
  - multi-stream files with `Oil`, `Gas`, and `Water`
  - optional pressure columns such as `Pressure` / `TubingPressure`
- Wells are cleaned once, then forecasted in parallel at the well level.
- Primary forecast streams currently emitted:
  - `Oil` ARPS rows when oil history exists
  - `Gas` ARPS rows when gas history exists
  - legacy `Production` ARPS rows for single-series files
- Derived ratio rows currently emitted:
  - `WOR = Water / Oil` as a flat exported ratio row when both streams exist

## Primary Artifacts

Each run now writes:

- `arps_parameters.csv`
- `arps_aries_phdwin.csv`
- `forecast_parameters.csv`
- `forecast_parameters.json`
- `forecast_run_summary.json`
- chart PNGs under `Charts/`
- optional `forecast-charts.pdf`

`forecast_parameters.csv` is the new flat export intended for storage and downstream integration. It includes both ARPS parameter rows and derived ratio rows.

# ArpsForecasting Project Schematic and File Interactions

## Sample Test Harness

Run an end-to-end validation against the sample CSV data:

```bash
dotnet run -- --harness
```

Optional paths:

```bash
dotnet run -- --harness /path/to/production_data.csv /path/to/Charts
```

Harness checks:
- sample CSV is readable
- wells parse and clean correctly
- chart PNGs are generated
- `arps_parameters.csv` is generated with model rows
- `forecast_parameters.csv` is generated with flat rows

Default output behavior:
- default harness input is `harness/input/production_data.csv`
- default harness charts are written to `harness/output/Charts/`
- default harness ARPS CSV is written to `harness/output/arps_parameters.csv`
- default harness flat export is written to `harness/output/forecast_parameters.csv`
- if `harness/input/production_data.csv` is missing, it is auto-seeded from root `production_data.csv` (if present)

## Local Web Harness

Run the browser-based local harness:

```bash
dotnet run -- --web
```

Then open `http://localhost:5000`.

Web harness behavior:
- upload a production CSV through the browser
- or point the app at an existing local CSV path on the machine running the server
- queue a forecast job that wraps `Program.RunForecast(...)`
- store each job under `runtime/forecast-jobs/<job-id>/`
- expose generated charts, `arps_parameters.csv`, `arps_aries_phdwin.csv`, `forecast_parameters.csv`, `forecast_parameters.json`, `forecast_run_summary.json`, and optional PDF artifacts for download

Large local runs:
- one CSV can contain hundreds of wells; the job runner will process them in one batch
- for larger runs, prefer local-path submission over browser upload when the file is already on disk
- runtime job metadata and artifacts remain file-based; PostgreSQL is not required for this local workflow

This document outlines the interactions between the files in the `ArpsForecasting` project as of March 20, 2025.

## File Overview and Interactions

### 1. ArpsForecaster.cs
- **Purpose**: Abstract base class defining the structure for decline curve forecasting (Exponential, Hyperbolic, Hyperbolic-to-Exponential).
- **Key Features**: Defines properties (`Qi`, `Di`, `B`, etc.) and abstract methods for fitting and forecasting.
- **Interactions**:
  - Extended by `SSE.cs` to implement specific decline curve logic.

### 2. SSE.cs
- **Purpose**: Concrete implementation of `ArpsForecaster` for decline curve analysis with annual decline percentage calculation.
- **Key Features**: Implements fitting methods and forecasting logic for all decline types, uses `ForecastConfig`.
- **Interactions**:
  - Used by `HybridForecaster.cs` for ARPS forecasting.
  - Used by `WellChartGenerator.cs` to fit ARPS curves to Prophet-like and Hybrid forecasts.

### 3. ForecastConfig.cs
- **Purpose**: Configuration class holding parameters for forecasting (e.g., `InitialRate`, `BFactor`, `TerminalDecline`).
- **Interactions**:
  - Used by `SSE.cs` and `HybridForecaster.cs` to parameterize forecasting models.
  - Instantiated in `WellChartGenerator.cs` to configure `HybridForecaster`.

### 4. HybridForecaster.cs
- **Purpose**: Combines ARPS (via `SSE`) with Random Forest to adjust residuals and improve forecasts.
- **Key Features**: Uses ML.NET for Random Forest regression on ARPS residuals.
- **Interactions**:
  - Depends on `SSE.cs` for base ARPS forecasts.
  - Uses `ForecastConfig.cs` for configuration.
  - Called by `WellChartGenerator.cs` to generate hybrid forecasts.

### 5. ProphetLikeForecaster.cs
- **Purpose**: Implements a simplified Prophet-like model with trend and seasonality components.
- **Key Features**: Uses ML.NET for trend modeling and custom Fourier series for seasonality.
- **Interactions**:
  - Instantiated and used by `WellChartGenerator.cs` for Prophet-like forecasts.

### 6. WellChartGenerator.cs
- **Purpose**: Generates and saves production forecast charts for wells using multiple models.
- **Key Features**: Integrates Prophet-like, Hybrid (ARPS + RF), and ARPS-only forecasts, outputs charts and parameters.
- **Interactions**:
  - Uses `ProphetLikeForecaster.cs` for Prophet-like forecasts.
  - Uses `HybridForecaster.cs` for hybrid forecasts.
  - Uses `SSE.cs` to fit ARPS curves to both Prophet-like and Hybrid forecasts.
  - Uses `ForecastConfig.cs` for Hybrid configuration.

### 7. Program.cs
- **Purpose**: Entry point of the application, loads well data, and orchestrates chart generation.
- **Key Features**: Reads CSV data, calls `WellChartGenerator`, and saves ARPS parameters.
- **Interactions**:
  - Instantiates `WellChartGenerator.cs` for each well.
  - Does not directly reference other forecasters.

## Detailed Schematic of Interactions

```plaintext
Program.cs
   └── Loads well data from CSV
       └── Instantiates WellChartGenerator.cs (per well)
              ├── Calls GenerateAndSaveChart()
              │    ├── Instantiates ProphetLikeForecaster.cs
              │    │    └── Forecasts production (trend + seasonality)
              │    ├── Instantiates ForecastConfig.cs
              │    │    └── Configures HybridForecaster.cs
              │    ├── Instantiates HybridForecaster.cs
              │    │    ├── Uses SSE.cs (extends ArpsForecaster.cs)
              │    │    │    └── Generates base ARPS forecasts
              │    │    └── Adjusts residuals with Random Forest
              │    ├── Uses SSE.cs (directly)
              │    │    └── Fits ARPS to Prophet and Hybrid forecasts
              │    └── Saves charts and ARPS parameters
              └── Collects ARPS parameters

ArpsForecaster.cs
   └── Abstract base class extended by SSE.cs

ForecastConfig.cs
   └── Provides configuration to SSE.cs and HybridForecaster.cs
   ```
   
### ---- Most Recent Changes Made ----
- Removed all references to `RandomForestForecaster.cs` from the file list, schematic, analysis, and recommendations.
- Simplified the analysis section since there are no unreferenced files to discuss.
- Kept the structure and formatting consistent with the previous version for ease of use.

!
