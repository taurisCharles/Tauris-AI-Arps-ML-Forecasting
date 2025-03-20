# ArpsForecasting Project Schematic and File Interactions

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

Below is a detailed ASCII representation of how the files interrelate in Markdown:

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