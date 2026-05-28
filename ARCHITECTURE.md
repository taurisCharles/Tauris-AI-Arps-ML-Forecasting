# TaurisAI-Forecasting Architecture

## Goal

Build `TaurisAI-Forecasting` as a web application backed by a forecasting engine that can later be deployed within the Tauris-AI platform for both interactive and batch forecasting workflows.

## High-Level Components

### 1. Web App

Responsibilities:

- data upload and run initiation,
- forecast review UI,
- custom chart control,
- QC and parameter presentation,
- export actions.

Suggested stack:

- React-based frontend,
- typed API client,
- reusable chart control module.

### 2. Forecast API

Responsibilities:

- accept run requests,
- validate inputs,
- orchestrate forecast jobs,
- expose run status,
- return results and artifacts.

Suggested stack:

- ASP.NET Core Web API

### 3. Forecast Engine

Responsibilities:

- time-series normalization,
- cadence detection,
- well-state classification,
- decline fitting,
- hybrid ML adjustment,
- model scoring and champion selection,
- forecast output generation.

This should remain callable as a library as well as through the API.

### 4. Batch Worker

Responsibilities:

- process larger multi-well jobs,
- support incremental reruns,
- publish batch outputs,
- support scheduled daily refreshes later.

### 5. Plugin Runtime

Responsibilities:

- allow user-supplied forecasting algorithms,
- execute them under a stable contract,
- isolate them from the core service.

Expected modes:

- native .NET implementations,
- Python-based implementations via subprocess or container boundary.

### 6. Persistence Layer

Responsibilities:

- store run metadata,
- store well forecast outputs,
- store QC outcomes,
- store prior-vs-new deltas,
- store guardrail profiles,
- store model/plugin version references.

Suggested storage:

- relational database for metadata,
- object storage for artifacts,
- optional parquet outputs for batch tables.

## Logical Modules

Suggested code organization:

- `web/`
- `api/`
- `engine/`
- `plugins/`
- `contracts/`
- `docs/`

Within `engine/`, likely submodules:

- `ingestion`
- `signals`
- `classification`
- `decline`
- `hybrid_ml`
- `selection`
- `economics`
- `publishing`

## Data Flow

1. User uploads or selects source data.
2. Web app posts run request to API.
3. API validates request and enqueues work.
4. Worker or engine processes impacted wells.
5. Engine computes forecasts and QC.
6. Results are persisted.
7. Web app reads back:
- time-series results,
- parameters,
- QC states,
- artifacts.

## Integration with Tauris

Near term:

- standalone web app and forecasting service

Later:

- Tauris sends daily refreshed well data to the forecasting service,
- service recalculates impacted wells,
- service returns updated forecasts and economics,
- Tauris rolls those into fund and corporate views.

`Tauris.PhdWin` may be referenced for:

- deployment patterns,
- service conventions,
- hosting approach,
- auth patterns.

But forecasting logic and chart behavior should remain independent and purpose-built.

## Deployment Direction

The architecture should support two operating modes:

- interactive single-user web use,
- scheduled or triggered batch recalculation.

This means:

- stateless API nodes,
- worker-based job execution,
- persisted result artifacts,
- clear model/plugin versioning.

## Guardrails / Config

Guardrails should not be hardcoded into fitting classes.

They should be stored as versioned profiles keyed by:

- basin,
- play,
- operator,
- lift type,
- or customer-defined group.

Examples:

- min/max `b`,
- min/max `Di`,
- terminal decline defaults,
- max segment count,
- minimum history thresholds,
- smoothing settings,
- QC tolerance thresholds.

## Plugin Model

User algorithms should conform to a common contract:

- input well series + metadata + config,
- output forecasted time series + parameters + diagnostics.

Plugin execution should support:

- versioning,
- timeout,
- memory limits,
- audit logging.

## Immediate Build Recommendation

Phase 1 should build:

- web UI shell,
- chart control scaffold,
- simple upload path,
- native forecast engine endpoint,
- JSON guardrail profiles,
- single-well interactive workflow.

Phase 2 should add:

- batch reruns,
- plugin system,
- economics layer,
- Tauris-triggered daily refresh flow.
