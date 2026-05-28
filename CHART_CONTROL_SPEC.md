# Forecast Chart Control Specification

## Purpose

The forecast chart control is a first-class product component for `TaurisAI-Forecasting`.

It should support rapid technical review of well performance and forecast behavior for both daily and monthly data.

This should not be treated as a generic off-the-shelf chart wrapper. It should be designed specifically for decline analysis and forecast QC workflows.

## Core Use Cases

- view historical production only,
- overlay one or more forecast methods,
- inspect fit boundaries and segment breaks,
- compare daily and monthly behavior,
- zoom into operational windows,
- switch between engineering display modes,
- evaluate QC visually.

## Required Data Layers

- historical production,
- smoothed production,
- forecast curve(s),
- uncertainty bands,
- fit start marker,
- segment break markers,
- terminal decline switch marker,
- optional annotations,
- optional prior forecast overlay.

## Supported Display Modes

- linear rate vs time,
- log rate vs time,
- normalized rate vs time,
- aligned-to-percent-of-peak mode,
- optional cumulative views later.

## Required Controls

### Navigation

- pan horizontally,
- box zoom,
- reset zoom,
- zoom to full history,
- zoom to fit window,
- zoom to recent period,
- jump to selected date range.

### Scaling

- linear / log toggle,
- user-defined number of log cycles,
- y-axis auto / manual limits,
- x-axis auto / manual date window,
- phase-specific scaling presets.

### Alignment / Normalization

- align to peak,
- align to `50% of peak`,
- align to fit start,
- compare normalized versus raw mode.

### Curve Controls

- toggle history / smoothed history,
- toggle methods on/off,
- toggle uncertainty bands,
- toggle prior forecast,
- toggle annotations and markers.

## Keyboard Shortcuts

The control should support configurable shortcut keys.

Initial desired behaviors:

- reset zoom,
- toggle log scale,
- toggle linear scale,
- next well,
- previous well,
- next method,
- previous method,
- zoom to fit window,
- zoom to recent data,
- toggle alignment mode,
- toggle annotations.

Shortcut definitions should be configurable, not hardcoded deep in rendering logic.

## Engineering Display Features

- segment boundaries should be visually marked,
- tail-switch points should be visually marked,
- tooltips should show exact values at cursor,
- crosshair should track date and rate,
- fit parameters for active curve should be visible in-context,
- plotted dates must support both daily and monthly cadence cleanly.

## Performance Expectations

The control should remain responsive when:

- switching between wells quickly,
- rendering multiple overlays,
- loading long daily histories,
- zooming repeatedly during technical review.

## Architecture Guidance

The chart control should be implemented as its own reusable component/module with:

- a stable data contract,
- rendering isolated from forecast engine logic,
- keyboard interaction layer,
- support for future annotations and saved views.

## Deferred but Expected Later

- manual drag fit boundaries,
- user-added segment markers,
- forecast lock points,
- comparison to analog wells,
- saved chart states,
- collaborative comments,
- print/export layouts.

## Design Input

This document is intentionally incomplete on visual design details. User-provided screenshots and interaction examples should be used to refine this specification before implementation begins.
