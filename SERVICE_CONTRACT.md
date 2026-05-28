# TaurisAI-Forecasting Service Contract

## Purpose

`TaurisAI-Forecasting` is a forecasting service responsible for:

- ingesting refreshed well-level operating data,
- determining which wells require re-evaluation,
- recalculating production forecasts,
- scoring forecast quality and exception risk,
- recomputing well-level economics,
- publishing updated well, fund, and portfolio forecast outputs back to Tauris.

This service is intended to support daily or near-daily refresh workflows as new field, accounting, and operational data arrive.

## Core Principles

- Forecasts should be recalculated incrementally, not by blindly refitting every well every time.
- Wells should be re-evaluated using both decline behavior and operational context.
- The service should preserve an audit trail of prior runs, model choices, and forecast deltas.
- Economics should be downstream of the selected production forecast, not mixed into the forecasting engine itself.
- The service should publish machine-readable statuses for UI workflows such as `green / yellow / red`.

## Service Boundary

`TaurisAI-Forecasting` owns:

- production history normalization,
- optional pressure-aware signal conditioning,
- change detection,
- well-level model fitting,
- champion/challenger selection,
- uncertainty scoring,
- forecast status classification,
- forecast output publication,
- economic recalculation inputs and well-level cash flow outputs.

It does not own:

- source-system master data entry,
- long-term corporate planning assumptions like G&A, hedging, debt, or financing,
- final portfolio/corporate reporting presentation,
- manual reserves engineering approval workflows.

## Trigger Model

The primary trigger is:

- Tauris receives new daily production and/or operating data for one or more wells.

Secondary triggers:

- new price deck,
- revised ownership / NRI / WI,
- revised LOE or tax assumptions,
- manual user rerun,
- backfill / correction of historical data,
- model configuration change.

## High-Level Workflow

1. Tauris lands refreshed source data.
2. Tauris calls `TaurisAI-Forecasting` with a run request.
3. The service identifies impacted wells.
4. The service cleans and assembles the latest time series.
5. The service detects material changes in well behavior.
6. The service fits or updates candidate models.
7. The service selects a champion model and scores confidence.
8. The service computes updated economic outputs.
9. The service returns:
   - updated well forecasts,
   - QC statuses,
   - deltas versus prior run,
   - portfolio-ready aggregated outputs.

## Input Contract

The service should accept a batch request shaped like:

```json
{
  "runId": "uuid",
  "asOfDate": "2026-05-14",
  "triggerType": "daily_data_refresh",
  "scope": {
    "fundIds": ["fund-1"],
    "wellIds": ["well-123", "well-456"]
  },
  "options": {
    "recalculateEconomics": true,
    "forceRefit": false,
    "publishOutputs": true
  }
}
```

### Required Upstream Data Per Well

- `WellId`
- `WellName`
- `API10`
- `AsOfDate`
- `Date`
- `GrossOil`
- `GrossGas`
- `GrossWater`

### Strongly Recommended Operating / Signal Inputs

- `TubingPressure`
- `CasingPressure`
- `PumpInletPressure` or `PumpIntakePressure`
- `FlowingPressure` if available
- `DowntimeHours` or `RunStatus`
- `ArtificialLiftType`
- `FluidLevel`
- `Choke`
- `WorkoverFlag`

### Economics / Ownership Inputs

- `NRI`
- `WI`
- `PriceDeckId`
- `OilDifferential`
- `GasDifferential`
- `NGLYield` or `NGL assumptions`
- `ProductionTaxRules`
- `AdValoremRules`
- `FixedLOE`
- `VariableLOE`
- `WorkoverBudget` or `Workover assumptions`

### Metadata Inputs

- `Basin`
- `Play`
- `County`
- `State`
- `CompletionDate`
- `FirstProductionDate`
- `WellType`
- `Operator`

## Internal Well-State Classification

Before fitting, each well should be classified into one of several operational states:

- `stable_decline`
- `early_time_cleanup`
- `post_workover_reset`
- `downtime_distorted`
- `pressure_constrained`
- `artificial_lift_constrained`
- `insufficient_history`
- `manual_review`

This state influences which models are eligible and how aggressive the service should be in refitting.

## Forecast Model Ladder

The forecasting engine should evaluate a model ladder, not a single model:

- exponential decline,
- hyperbolic decline,
- hyperbolic to exponential terminal decline,
- segmented decline,
- pressure-aware decline adjustment,
- residual ML correction on top of decline baseline,
- analog / archetype fallback for sparse wells.

Champion selection should be based on:

- rolling backtest error,
- tail fit quality,
- operational consistency,
- stability of parameters,
- regime classification.

## Output Contract

The core result should return:

```json
{
  "runId": "uuid",
  "asOfDate": "2026-05-14",
  "status": "completed",
  "summary": {
    "wellsRequested": 250,
    "wellsProcessed": 243,
    "wellsFailed": 7,
    "wellsGreen": 180,
    "wellsYellow": 45,
    "wellsRed": 18
  },
  "artifacts": {
    "wellForecastPath": "s3://.../well_forecasts.parquet",
    "economicsPath": "s3://.../well_economics.parquet",
    "exceptionsPath": "s3://.../exceptions.parquet"
  }
}
```

## Required Per-Well Output Fields

- `RunId`
- `AsOfDate`
- `WellId`
- `WellName`
- `API10`
- `Scenario`
- `ForecastDate`
- `ModelName`
- `ModelVersion`
- `WellState`
- `QcStatus`
- `QcReason`
- `GrossOilForecast`
- `GrossGasForecast`
- `GrossWaterForecast`
- `NetOilForecast`
- `NetGasForecast`
- `NetNGLForecast`
- `OilPrice`
- `GasPrice`
- `NGLPrice`
- `NetRevenueOil`
- `NetRevenueGas`
- `NetRevenueNGL`
- `NetProductionTaxes`
- `NetAdValorem`
- `FixedLOE`
- `VariableLOE`
- `Workover`
- `NetIncome`
- `PriorForecastNetIncome`
- `DeltaNetIncome`

## QC / Traffic Light Contract

Each well should emit one of:

- `green`
- `yellow`
- `red`

Suggested interpretation:

- `green`: stable fit, low backtest error, no material operational anomalies.
- `yellow`: usable fit with caution; some instability, missing signals, or model disagreement.
- `red`: strong exception; likely operational reset, poor data quality, or nonstationary behavior requiring review.

Suggested machine-readable QC reasons:

- `insufficient_history`
- `high_backtest_error`
- `recent_change_point`
- `pressure_shift`
- `downtime_distortion`
- `workover_detected`
- `forecast_boundary_jump`
- `model_disagreement`
- `missing_required_inputs`

## Delta / Change Detection Contract

The service should compare each new forecast to the prior published forecast and compute:

- `DeltaQi`
- `DeltaDi`
- `DeltaB`
- `Delta30DayOil`
- `Delta12MonthOil`
- `DeltaEUR`
- `DeltaPV` if economics layer includes valuation metrics
- `DeltaNetIncome`

This allows Tauris to surface:

- wells that changed materially,
- forecast changes before accounting fully reflects them,
- operational anomalies that warrant attention.

## Economics Contract

Economics should be computed after the forecast selection step.

Minimum well-level economic outputs:

- `NetRevenueOil`
- `NetRevenueGas`
- `NetRevenueNGL`
- `NetProductionTaxes`
- `NetAdValorem`
- `FixedLOE`
- `VariableLOE`
- `Workover`
- `NetIncome`

These outputs should feed a separate downstream corporate model where Tauris layers in:

- G&A,
- hedge settlements,
- financing effects,
- debt service,
- corporate overhead,
- planning scenarios.

## Performance Expectations

Target operating goal:

- re-evaluate thousands of wells in minutes, not hours.

Suggested engineering expectations:

- incremental rerun support,
- parallel well processing,
- persisted intermediate artifacts,
- deterministic model versioning,
- separate batch and interactive execution modes.

## Persistence / Audit

Each run should persist:

- request payload,
- source snapshot reference,
- model version,
- chosen model per well,
- QC status,
- final output tables,
- prior-to-new deltas.

This is required for:

- user trust,
- model debugging,
- reproducibility,
- exception review,
- portfolio attribution.

## Suggested Repository Direction

`TaurisAI-Forecasting` should evolve into:

- a reusable forecasting library,
- a batch runner,
- and eventually a service endpoint callable by Tauris.

Suggested logical modules:

- `ingestion`
- `signals`
- `classification`
- `decline`
- `hybrid_ml`
- `selection`
- `economics`
- `publishing`
- `contracts`

## Near-Term Implementation Priority

1. Stabilize well input schema.
2. Add explicit well-state classification.
3. Standardize QC outputs and traffic-light logic.
4. Separate raw decline fitting from economics.
5. Add prior-vs-new forecast delta publication.
6. Add batch orchestration for daily reruns.
