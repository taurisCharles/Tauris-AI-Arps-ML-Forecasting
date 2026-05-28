# TaurisAI-Forecasting Product Requirements

## Purpose

`TaurisAI-Forecasting` will be a web application for loading well production data, visualizing history, fitting forecast models, comparing methods, reviewing QC, and exporting results.

The first product goal is to provide a high-quality interactive forecasting experience for technical users. The second goal is to operationalize that forecasting engine for batch recalculation inside the broader Tauris platform.

## Product Scope

The application should support:

- loading user-provided production data,
- plotting daily and monthly production history,
- running multiple forecast methods,
- comparing forecast outputs visually and numerically,
- enforcing geographic and engineering guardrails,
- supporting custom user forecasting algorithms,
- exporting forecast parameters and forecasted time series,
- later supporting automated daily reruns from Tauris source data.

## Primary User Workflow

1. User opens the web app.
2. User uploads or selects a dataset.
3. User maps required input fields if needed.
4. User selects a well and target phase.
5. User views the production plot.
6. User selects forecast methods and guardrail profile.
7. User runs or refreshes the forecast.
8. User reviews:
- fitted parameters,
- QC status,
- model comparisons,
- chart overlays.
9. User adjusts fit settings if needed.
10. User exports forecast outputs.

## Initial Screens

### 1. Dataset / Run Screen

Purpose:

- upload source files,
- preview parsed wells,
- select cadence and field mapping,
- launch forecast runs.

Key UI elements:

- file upload,
- dataset summary,
- column mapping panel,
- cadence auto-detect override,
- run button,
- run status panel.

### 2. Forecast Review Screen

Purpose:

- visualize production and forecast,
- inspect fitted parameters,
- compare methods,
- review QC.

Key UI elements:

- custom forecast chart control,
- well selector,
- phase selector,
- method selector,
- guardrail profile selector,
- QC summary panel,
- parameter grid,
- model comparison panel.

### 3. Batch / Portfolio Screen

Purpose:

- review multi-well forecast results,
- identify exceptions,
- compare changes against prior run.

Key UI elements:

- run summary,
- wells table,
- green/yellow/red counts,
- filterable exception queue,
- delta metrics,
- export controls.

## Supported Input Types

### Required

- `WellName`
- `Date`
- at least one target rate series:
  - `Oil`
  - `Gas`
  - `Water`

### Optional but Valuable

- `API10`
- `TubingPressure`
- `CasingPressure`
- `PumpInletPressure` or `PumpIntakePressure`
- `Downtime`
- `LiftType`
- `Basin`
- `Operator`

## Forecasting Requirements

The application should support multiple methods:

- exponential,
- hyperbolic,
- hyperbolic to exponential tail,
- segmented decline,
- hybrid decline plus ML residual correction,
- user-provided custom model.

The app should expose:

- method selection,
- forecast horizon,
- fit start behavior,
- terminal decline assumptions,
- segment limits,
- guardrail profile selection.

## QC Requirements

Each forecast should emit:

- `green`,
- `yellow`,
- `red`

with machine-readable reasons and user-readable explanation.

QC should consider:

- backtest fit quality,
- curve stability,
- parameter reasonableness,
- recent change points,
- pressure / downtime anomalies,
- model disagreement.

## Export Requirements

The app should export:

- forecast time series,
- fitted parameters,
- QC summary,
- method comparison metrics,
- later, well economics.

## Reference Constraint

`Tauris.PhdWin` can be used as a reference for deployment and application patterns, but `TaurisAI-Forecasting` should define its own custom chart control and forecasting-specific UX rather than inherit generic plotting behavior.
