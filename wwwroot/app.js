const form = document.getElementById("forecast-form");
const fileInput = document.getElementById("csv-file");
const localCsvPathInput = document.getElementById("local-csv-path");
const phaseModeSelect = document.getElementById("phase-mode");
const generatePdfCheckbox = document.getElementById("generate-pdf");
const submitButton = document.getElementById("submit-button");
const formStatus = document.getElementById("form-status");
const jobsEmpty = document.getElementById("jobs-empty");
const jobsList = document.getElementById("jobs-list");
const refreshButton = document.getElementById("refresh-button");
const jobTemplate = document.getElementById("job-template");
const metricCardTemplate = document.getElementById("metric-card-template");
const healthPill = document.getElementById("health-pill");
const activeJobPill = document.getElementById("active-job-pill");
const tabUpload = document.getElementById("tab-upload");
const tabLocal = document.getElementById("tab-local");
const uploadFields = document.getElementById("upload-fields");
const localFields = document.getElementById("local-fields");
const refreshLocalFilesButton = document.getElementById("refresh-local-files");
const localFilesEmpty = document.getElementById("local-files-empty");
const localFilesList = document.getElementById("local-files-list");
const reviewWorkspace = document.getElementById("review-workspace");
const jobSummaryLine = document.getElementById("job-summary-line");
const reviewJobName = document.getElementById("review-job-name");
const reviewJobStatus = document.getElementById("review-job-status");
const wellList = document.getElementById("well-list");
const selectedWellName = document.getElementById("selected-well-name");
const selectedWellMeta = document.getElementById("selected-well-meta");
const phaseTabs = document.getElementById("phase-tabs");
const forecastChart = document.getElementById("forecast-chart");
const chartEmpty = document.getElementById("chart-empty");
const chartCaption = document.getElementById("chart-caption");
const metricCards = document.getElementById("metric-cards");
const lateralLengthValue = document.getElementById("lateral-length-value");
const twostreamEurValue = document.getElementById("twostream-eur-value");
const pressureFlagValue = document.getElementById("pressure-flag-value");
const countGreen = document.getElementById("count-green");
const countYellow = document.getElementById("count-yellow");
const countRed = document.getElementById("count-red");
const firstWellButton = document.getElementById("first-well-button");
const prevWellButton = document.getElementById("prev-well-button");
const nextWellButton = document.getElementById("next-well-button");
const lastWellButton = document.getElementById("last-well-button");
const fitStartShift = document.getElementById("fit-start-shift");
const fitStartShiftValue = document.getElementById("fit-start-shift-value");
const diBias = document.getElementById("di-bias");
const diBiasValue = document.getElementById("di-bias-value");
const bBias = document.getElementById("b-bias");
const bBiasValue = document.getElementById("b-bias-value");
const reviewNotes = document.getElementById("review-notes");
const saveReviewButton = document.getElementById("save-review-button");
const clearReviewButton = document.getElementById("clear-review-button");
const savedReviewPill = document.getElementById("saved-review-pill");
const qcButtons = [...document.querySelectorAll(".qc-button")];
const filterChips = [...document.querySelectorAll(".filter-chip")];

let pollHandle = null;
let submissionMode = "upload";
let reviewDirty = false;

const state = {
  jobs: [],
  selectedJobId: null,
  selectedWellName: null,
  selectedPhase: null,
  qcFilter: "all",
  reviewCache: new Map()
};

form.addEventListener("submit", handleSubmit);
refreshButton.addEventListener("click", () => loadJobs(true));
tabUpload.addEventListener("click", () => setSubmissionMode("upload"));
tabLocal.addEventListener("click", () => setSubmissionMode("local"));
refreshLocalFilesButton.addEventListener("click", () => loadLocalFiles());
fileInput.addEventListener("change", handleAutoUpload);
localCsvPathInput.addEventListener("keydown", event => {
  if (event.key === "Enter") {
    event.preventDefault();
    handleSubmit(event);
  }
});
firstWellButton.addEventListener("click", () => moveSelection("first"));
prevWellButton.addEventListener("click", () => moveSelection(-1));
nextWellButton.addEventListener("click", () => moveSelection(1));
lastWellButton.addEventListener("click", () => moveSelection("last"));
saveReviewButton.addEventListener("click", saveCurrentReview);
clearReviewButton.addEventListener("click", clearCurrentReview);
fitStartShift.addEventListener("input", () => handleReviewControlChange("fitStartShift"));
diBias.addEventListener("input", () => handleReviewControlChange("diBias"));
bBias.addEventListener("input", () => handleReviewControlChange("bBias"));
reviewNotes.addEventListener("input", () => handleReviewControlChange("notes"));
filterChips.forEach(chip => {
  chip.addEventListener("click", () => {
    state.qcFilter = chip.dataset.filter || "all";
    renderFilterChips();
    renderReview();
  });
});
qcButtons.forEach(button => {
  button.addEventListener("click", () => {
    setReviewQc(button.dataset.qc || "yellow");
  });
});
document.addEventListener("keydown", handleKeyboardShortcuts);

bootstrap();

async function bootstrap() {
  await checkHealth();
  await loadLocalFiles();
  await loadJobs(false);
  startPolling();
}

async function checkHealth() {
  try {
    const response = await fetch("/api/health");
    if (!response.ok) throw new Error("health check failed");
    healthPill.textContent = "Service Ready";
    healthPill.className = "pill success";
  } catch {
    healthPill.textContent = "Service Unavailable";
    healthPill.className = "pill failed";
  }
}

async function handleAutoUpload() {
  if (submissionMode === "upload" && fileInput.files && fileInput.files.length > 0) {
    await handleSubmit(new Event("submit"));
  }
}

async function handleSubmit(event) {
  event.preventDefault();

  submitButton.disabled = true;
  setFormStatus(
    submissionMode === "upload"
      ? "Uploading CSV and starting forecast run."
      : "Starting local CSV forecast run.",
    false
  );

  try {
    const response = submissionMode === "upload"
      ? await submitUploadJob()
      : await submitLocalPathJob();
    const data = await response.json();
    if (!response.ok) {
      throw new Error(data.error || "Forecast job request failed.");
    }

    state.selectedJobId = data.jobId;
    setActiveJob(data.jobId, "Queued");
    setFormStatus(`Job ${data.jobId} queued. Waiting for completion.`, false);
    form.reset();
    phaseModeSelect.value = "4";
    generatePdfCheckbox.checked = false;
    setSubmissionMode(submissionMode);
    await loadJobs(true);
  } catch (error) {
    setFormStatus(error.message, true);
  } finally {
    submitButton.disabled = false;
  }
}

async function loadJobs(forceReviewRefresh) {
  try {
    const response = await fetch("/api/forecast-jobs");
    if (!response.ok) throw new Error("Failed to load jobs.");

    const jobs = await response.json();
    state.jobs = jobs;
    renderJobs(jobs);
    updateActiveJobBanner(jobs);

    const preferredJob = selectPreferredJob(jobs);
    if (!preferredJob) {
      reviewWorkspace.classList.add("hidden");
      return;
    }

    if (preferredJob.jobId !== state.selectedJobId || forceReviewRefresh) {
      state.selectedJobId = preferredJob.jobId;
    }

    if (preferredJob.status === "Completed") {
      await ensureReviewLoaded(preferredJob, forceReviewRefresh);
      reviewWorkspace.classList.remove("hidden");
      renderReview();
    } else {
      reviewWorkspace.classList.add("hidden");
      setFormStatus(`Job ${preferredJob.jobId} is ${preferredJob.status.toLowerCase()}. Review opens automatically on completion.`, false);
    }
  } catch (error) {
    setFormStatus(error.message, true);
  }
}

async function loadLocalFiles() {
  try {
    const response = await fetch("/api/local-csvs");
    if (!response.ok) throw new Error("Failed to load local CSV candidates.");
    const files = await response.json();
    renderLocalFiles(files);
  } catch (error) {
    localFilesEmpty.textContent = error.message;
    localFilesEmpty.style.display = "block";
    localFilesList.innerHTML = "";
  }
}

function renderJobs(jobs) {
  jobsList.innerHTML = "";
  jobsEmpty.style.display = jobs.length === 0 ? "block" : "none";

  for (const job of jobs) {
    const fragment = jobTemplate.content.cloneNode(true);
    fragment.querySelector(".job-title").textContent = job.originalFileName || "Unnamed Upload";
    fragment.querySelector(".job-meta").textContent = `${formatUtc(job.createdAtUtc)} · ${job.submissionKind} · ${job.result?.forecastParameterCount || 0} flat rows`;

    const statusPill = fragment.querySelector(".job-status");
    statusPill.textContent = job.status;
    statusPill.classList.add(statusClass(job.status));

    const button = fragment.querySelector(".job-row-button");
    button.addEventListener("click", async () => {
      state.selectedJobId = job.jobId;
      if (job.status === "Completed") {
        await ensureReviewLoaded(job, true);
        reviewWorkspace.classList.remove("hidden");
        renderReview();
      } else {
        setFormStatus(`Job ${job.jobId} is ${job.status.toLowerCase()}.`, false);
      }
    });

    jobsList.appendChild(fragment);
  }
}

function renderLocalFiles(files) {
  localFilesList.innerHTML = "";
  localFilesEmpty.style.display = files.length === 0 ? "block" : "none";
  if (files.length === 0) {
    return;
  }

  for (const file of files) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "local-file-button";
    button.textContent = file;
    button.addEventListener("click", async () => {
      localCsvPathInput.value = file;
      setSubmissionMode("local");
      await handleSubmit(new Event("submit"));
    });
    localFilesList.appendChild(button);
  }
}

function renderReview() {
  const review = state.reviewCache.get(state.selectedJobId);
  if (!review || review.wells.length === 0) {
    reviewWorkspace.classList.add("hidden");
    return;
  }

  const filteredWells = getFilteredWells(review);
  if (filteredWells.length === 0) {
    wellList.innerHTML = `<div class="empty-state">No wells match the current filter.</div>`;
    return;
  }

  if (!filteredWells.some(well => well.name === state.selectedWellName)) {
    state.selectedWellName = filteredWells[0].name;
  }

  const selectedWell = review.wells.find(well => well.name === state.selectedWellName) || filteredWells[0];
  state.selectedWellName = selectedWell.name;
  if (!selectedWell.availablePhases.includes(state.selectedPhase)) {
    state.selectedPhase = selectedWell.availablePhases[0] || null;
  }

  reviewJobName.textContent = review.job.originalFileName || review.job.jobId;
  reviewJobStatus.textContent = review.job.status;
  reviewJobStatus.className = `pill ${statusClass(review.job.status)}`;
  jobSummaryLine.textContent = `${review.wells.length} wells · ${review.job.result?.forecastParameterCount || 0} flat rows · completed ${formatUtc(review.job.updatedAtUtc)}`;

  renderSidebarCounts(review);
  renderWellList(filteredWells, review);
  renderWellDetail(selectedWell, review);
}

function renderSidebarCounts(review) {
  const counts = { green: 0, yellow: 0, red: 0 };
  for (const well of review.wells) {
    const qc = getEffectiveQc(review.job.jobId, well);
    counts[qc] = (counts[qc] || 0) + 1;
  }
  countGreen.textContent = String(counts.green || 0);
  countYellow.textContent = String(counts.yellow || 0);
  countRed.textContent = String(counts.red || 0);
}

function renderWellList(filteredWells, review) {
  wellList.innerHTML = "";
  for (const well of filteredWells) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "well-list-item";
    if (well.name === state.selectedWellName) {
      button.classList.add("selected");
    }

    const qc = getEffectiveQc(review.job.jobId, well);
    button.innerHTML = `
      <span class="well-qc ${qc}"></span>
      <span class="well-name">${escapeHtml(well.name)}</span>
      <span class="well-subline">${well.availablePhases.join(" / ") || "No phases"}</span>
    `;
    button.addEventListener("click", () => {
      state.selectedWellName = well.name;
      state.selectedPhase = well.availablePhases[0] || null;
      loadReviewInputsForSelectedWell();
      renderReview();
    });
    wellList.appendChild(button);
  }
}

function renderWellDetail(well, review) {
  selectedWellName.textContent = well.name;
  selectedWellMeta.textContent = `${well.availablePhases.join(" / ") || "No phases"} · ${well.cadence} cadence · ${well.sourcePointCount} source rows`;
  lateralLengthValue.textContent = formatMetricValue(well.lateralLength, 0, "");
  pressureFlagValue.textContent = well.pressureUsed ? "Pressure Present" : "No pressure";

  renderPhaseTabs(well);
  renderChart(well, review);
  renderMetricCards(well, review);
  renderTwoStreamEur(well, review);
  loadReviewInputsForSelectedWell();
}

function renderPhaseTabs(well) {
  phaseTabs.innerHTML = "";
  for (const phase of well.availablePhases) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `phase-tab ${phase === state.selectedPhase ? "active-phase" : ""}`;
    button.textContent = phase;
    button.addEventListener("click", () => {
      state.selectedPhase = phase;
      renderReview();
    });
    phaseTabs.appendChild(button);
  }
}

function renderChart(well, review) {
  const chartArtifact = well.chartByMetric[state.selectedPhase] || null;
  if (!chartArtifact) {
    forecastChart.removeAttribute("src");
    forecastChart.style.display = "none";
    chartEmpty.style.display = "grid";
    chartCaption.textContent = `No chart artifact found for ${well.name}${state.selectedPhase ? ` [${state.selectedPhase}]` : ""}.`;
    return;
  }

  chartEmpty.style.display = "none";
  forecastChart.style.display = "block";
  forecastChart.src = chartArtifact.downloadUrl;
  forecastChart.alt = `${well.name} ${state.selectedPhase} forecast chart`;
  chartCaption.textContent = `${well.name} · ${state.selectedPhase} · current chart output`;
}

function renderMetricCards(well, review) {
  metricCards.innerHTML = "";
  const reviewState = getStoredReview(review.job.jobId, well.name);

  for (const metric of ["Oil", "Gas", "Water"]) {
    const fragment = metricCardTemplate.content.cloneNode(true);
    const metricName = fragment.querySelector(".metric-name");
    const metricStatus = fragment.querySelector(".metric-status");
    metricName.textContent = metric;

    const metricView = buildMetricView(well, metric, reviewState);
    metricStatus.textContent = metricView.statusLabel;
    metricStatus.className = `pill ${metricView.pillClass}`;
    fragment.querySelector(".metric-cum").textContent = metricView.cumDisplay;
    fragment.querySelector(".metric-eur").textContent = metricView.eurDisplay;
    fragment.querySelector(".metric-qi").textContent = metricView.qiDisplay;
    fragment.querySelector(".metric-di").textContent = metricView.diDisplay;
    fragment.querySelector(".metric-b").textContent = metricView.bDisplay;
    fragment.querySelector(".metric-model").textContent = metricView.modelDisplay;

    metricCards.appendChild(fragment);
  }
}

function renderTwoStreamEur(well, review) {
  const reviewState = getStoredReview(review.job.jobId, well.name);
  const oilView = buildMetricView(well, "Oil", reviewState);
  const gasView = buildMetricView(well, "Gas", reviewState);
  if (!isFiniteNumber(oilView.eurValue) || !isFiniteNumber(gasView.eurValue)) {
    twostreamEurValue.textContent = "n/a";
    return;
  }

  const boe = oilView.eurValue + (gasView.eurValue / 6.0);
  twostreamEurValue.textContent = `${formatMetricValue(boe, 0, "")} BOE`;
}

function buildMetricView(well, metric, reviewState) {
  const observedCum = well.observedCum[metric];
  if (metric === "Water") {
    const waterCum = isFiniteNumber(observedCum) ? observedCum : null;
    const waterRatio = well.primaryRowsByMetric.WOR || null;
    const oilPrimary = well.primaryRowsByMetric.Oil || null;
    let eur = null;
    if (waterRatio && oilPrimary) {
      const oilRemaining = estimateRemaining(oilPrimary, well.lastTime, well.cadence, reviewState);
      const ratioValue = waterRatio.forecastAnchorValue ?? waterRatio.latestObservedValue ?? null;
      if (isFiniteNumber(oilRemaining) && isFiniteNumber(ratioValue)) {
        eur = (waterCum || 0) + (oilRemaining * ratioValue);
      }
    }

    return {
      cumValue: waterCum,
      eurValue: eur,
      cumDisplay: formatMetricValue(waterCum, 0, well.unitsByMetric.Water || ""),
      eurDisplay: formatMetricValue(eur, 0, well.unitsByMetric.Water || ""),
      qiDisplay: "ratio-driven",
      diDisplay: "n/a",
      bDisplay: "n/a",
      modelDisplay: waterRatio ? waterRatio.modelType : "No ratio",
      statusLabel: waterRatio ? "Ratio" : "Missing",
      pillClass: waterRatio ? "running" : "muted"
    };
  }

  const primaryRow = well.primaryRowsByMetric[metric] || null;
  const eur = primaryRow ? estimateTotalEur(primaryRow, observedCum, well.lastTime, well.cadence, reviewState) : null;
  return {
    cumValue: observedCum,
    eurValue: eur,
    cumDisplay: formatMetricValue(observedCum, 0, well.unitsByMetric[metric] || ""),
    eurDisplay: formatMetricValue(eur, 0, well.unitsByMetric[metric] || ""),
    qiDisplay: formatMetricValue(primaryRow?.qi, 2, ""),
    diDisplay: formatPercent(primaryRow?.annualDiPercent),
    bDisplay: formatMetricValue(primaryRow?.b, 2, ""),
    modelDisplay: primaryRow?.modelType || "No model",
    statusLabel: primaryRow ? "Forecasted" : "Missing",
    pillClass: primaryRow ? "success" : "muted"
  };
}

function getFilteredWells(review) {
  if (state.qcFilter === "all") {
    return [...review.wells];
  }
  return review.wells.filter(well => getEffectiveQc(review.job.jobId, well) === state.qcFilter);
}

function renderFilterChips() {
  filterChips.forEach(chip => {
    chip.classList.toggle("active-filter", chip.dataset.filter === state.qcFilter);
  });
}

function moveSelection(direction) {
  const review = state.reviewCache.get(state.selectedJobId);
  if (!review) return;
  const filteredWells = getFilteredWells(review);
  if (filteredWells.length === 0) return;

  let index = filteredWells.findIndex(well => well.name === state.selectedWellName);
  if (index < 0) index = 0;

  if (direction === "first") index = 0;
  else if (direction === "last") index = filteredWells.length - 1;
  else index = Math.max(0, Math.min(filteredWells.length - 1, index + direction));

  state.selectedWellName = filteredWells[index].name;
  state.selectedPhase = filteredWells[index].availablePhases[0] || null;
  loadReviewInputsForSelectedWell();
  renderReview();
}

function handleKeyboardShortcuts(event) {
  const activeTag = document.activeElement?.tagName || "";
  if (activeTag === "INPUT" || activeTag === "TEXTAREA" || activeTag === "SELECT") {
    return;
  }

  switch (event.key) {
    case ",":
      event.preventDefault();
      moveSelection(-1);
      break;
    case ".":
      event.preventDefault();
      moveSelection(1);
      break;
    case "1":
      event.preventDefault();
      setReviewQc("green");
      break;
    case "2":
      event.preventDefault();
      setReviewQc("yellow");
      break;
    case "3":
      event.preventDefault();
      setReviewQc("red");
      break;
    case "[":
      event.preventDefault();
      nudgeRange(fitStartShift, -1);
      break;
    case "]":
      event.preventDefault();
      nudgeRange(fitStartShift, 1);
      break;
    case "-":
      event.preventDefault();
      nudgeRange(diBias, -0.01);
      break;
    case "=":
      event.preventDefault();
      nudgeRange(diBias, 0.01);
      break;
    case "b":
    case "B":
      event.preventDefault();
      nudgeRange(bBias, -0.01);
      break;
    case "n":
    case "N":
      event.preventDefault();
      nudgeRange(bBias, 0.01);
      break;
    case "s":
    case "S":
      event.preventDefault();
      saveCurrentReview();
      break;
  }
}

function nudgeRange(input, delta) {
  const value = Number(input.value);
  const step = Number(input.step) || 1;
  const next = clampNumber(value + delta, Number(input.min), Number(input.max));
  input.value = roundToStep(next, step);
  if (input === fitStartShift) handleReviewControlChange("fitStartShift");
  if (input === diBias) handleReviewControlChange("diBias");
  if (input === bBias) handleReviewControlChange("bBias");
}

function handleReviewControlChange(source) {
  reviewDirty = true;
  updateReviewInputsDisplay();
  if (source === "fitStartShift" || source === "diBias" || source === "bBias") {
    renderReview();
  } else {
    updateReviewSavePill();
  }
}

function updateReviewInputsDisplay() {
  fitStartShiftValue.textContent = fitStartShift.value;
  diBiasValue.textContent = Number(diBias.value).toFixed(2);
  bBiasValue.textContent = Number(bBias.value).toFixed(2);
  updateReviewSavePill();
  updateQcButtons();
}

function updateReviewSavePill() {
  if (reviewDirty) {
    savedReviewPill.textContent = "Unsaved review";
    savedReviewPill.className = "pill running";
    return;
  }

  const review = getCurrentReviewState();
  if (review.savedAtUtc) {
    savedReviewPill.textContent = `Saved ${formatUtc(review.savedAtUtc)}`;
    savedReviewPill.className = "pill success";
  } else {
    savedReviewPill.textContent = "No saved review";
    savedReviewPill.className = "pill muted";
  }
}

function updateQcButtons() {
  const qc = getCurrentReviewState().qc || null;
  qcButtons.forEach(button => {
    button.classList.toggle("active-qc", button.dataset.qc === qc);
  });
}

function loadReviewInputsForSelectedWell() {
  const review = getCurrentReviewState();
  fitStartShift.value = String(review.fitStartShift ?? 0);
  diBias.value = String(review.diBias ?? 0);
  bBias.value = String(review.bBias ?? 0);
  reviewNotes.value = review.notes || "";
  reviewDirty = false;
  updateReviewInputsDisplay();
}

function getCurrentReviewState() {
  if (!state.selectedJobId || !state.selectedWellName) {
    return {};
  }
  return getStoredReview(state.selectedJobId, state.selectedWellName);
}

function setReviewQc(qc) {
  if (!state.selectedJobId || !state.selectedWellName) return;
  const reviewMap = loadReviewMap(state.selectedJobId);
  const current = reviewMap[state.selectedWellName] || {};
  reviewMap[state.selectedWellName] = {
    ...current,
    qc
  };
  persistReviewMap(state.selectedJobId, reviewMap);
  reviewDirty = true;
  updateReviewInputsDisplay();
  renderReview();
}

function saveCurrentReview() {
  if (!state.selectedJobId || !state.selectedWellName) return;
  const reviewMap = loadReviewMap(state.selectedJobId);
  reviewMap[state.selectedWellName] = {
    ...reviewMap[state.selectedWellName],
    qc: reviewMap[state.selectedWellName]?.qc || deriveHeuristicQc(getCurrentWell()),
    fitStartShift: Number(fitStartShift.value),
    diBias: Number(diBias.value),
    bBias: Number(bBias.value),
    notes: reviewNotes.value.trim(),
    savedAtUtc: new Date().toISOString()
  };
  persistReviewMap(state.selectedJobId, reviewMap);
  reviewDirty = false;
  updateReviewInputsDisplay();
  renderReview();
}

function clearCurrentReview() {
  if (!state.selectedJobId || !state.selectedWellName) return;
  const reviewMap = loadReviewMap(state.selectedJobId);
  delete reviewMap[state.selectedWellName];
  persistReviewMap(state.selectedJobId, reviewMap);
  reviewDirty = false;
  loadReviewInputsForSelectedWell();
  renderReview();
}

function startPolling() {
  stopPolling();
  pollHandle = window.setInterval(() => loadJobs(false), 4000);
}

function stopPolling() {
  if (pollHandle !== null) {
    window.clearInterval(pollHandle);
    pollHandle = null;
  }
}

function statusClass(status) {
  switch ((status || "").toLowerCase()) {
    case "completed":
      return "success";
    case "failed":
      return "failed";
    case "running":
      return "running";
    default:
      return "muted";
  }
}

function setFormStatus(message, isError) {
  formStatus.textContent = message;
  formStatus.className = isError ? "status-line error" : "status-line";
}

function setSubmissionMode(mode) {
  submissionMode = mode;
  const uploadActive = mode === "upload";
  uploadFields.classList.toggle("hidden", !uploadActive);
  localFields.classList.toggle("hidden", uploadActive);
  tabUpload.classList.toggle("active-tab", uploadActive);
  tabLocal.classList.toggle("active-tab", !uploadActive);
  fileInput.required = uploadActive;
  localCsvPathInput.required = !uploadActive;
}

function setActiveJob(jobId, label) {
  activeJobPill.textContent = `${label}: ${jobId.slice(0, 8)}`;
  activeJobPill.className = "pill running";
}

function updateActiveJobBanner(jobs) {
  const runningJob = jobs.find(job => job.status === "Running" || job.status === "Uploaded");
  if (runningJob) {
    activeJobPill.textContent = `${runningJob.status}: ${runningJob.originalFileName || runningJob.jobId}`;
    activeJobPill.className = `pill ${statusClass(runningJob.status)}`;
    return;
  }

  const completedJob = jobs.find(job => job.status === "Completed");
  if (completedJob) {
    activeJobPill.textContent = `Ready: ${completedJob.originalFileName || completedJob.jobId}`;
    activeJobPill.className = "pill success";
    return;
  }

  activeJobPill.textContent = "No active job";
  activeJobPill.className = "pill muted";
}

function selectPreferredJob(jobs) {
  if (state.selectedJobId) {
    const existing = jobs.find(job => job.jobId === state.selectedJobId);
    if (existing) {
      return existing;
    }
  }

  return jobs.find(job => job.status === "Completed")
    || jobs.find(job => job.status === "Running" || job.status === "Uploaded")
    || jobs[0]
    || null;
}

async function ensureReviewLoaded(job, forceRefresh) {
  const cached = state.reviewCache.get(job.jobId);
  if (cached && !forceRefresh && cached.job.updatedAtUtc === job.updatedAtUtc) {
    if (!state.selectedWellName || !cached.wells.some(well => well.name === state.selectedWellName)) {
      state.selectedWellName = cached.wells[0]?.name || null;
      state.selectedPhase = cached.wells[0]?.availablePhases[0] || null;
    }
    return;
  }

  const parameterArtifact = findArtifact(job, artifact =>
    artifact.relativePath.endsWith("forecast_parameters.json"));
  const inputArtifact = findArtifact(job, artifact =>
    artifact.relativePath.startsWith("input/") && artifact.relativePath.endsWith(".csv"));

  if (!parameterArtifact || !inputArtifact) {
    throw new Error("Completed job is missing required review artifacts.");
  }

  const [rowsResponse, csvResponse] = await Promise.all([
    fetch(parameterArtifact.downloadUrl),
    fetch(inputArtifact.downloadUrl)
  ]);
  if (!rowsResponse.ok || !csvResponse.ok) {
    throw new Error("Failed to load review artifacts.");
  }

  const parameterRows = await rowsResponse.json();
  const csvText = await csvResponse.text();
  const inputRows = parseCsv(csvText);
  const review = buildReviewModel(job, parameterRows, inputRows);
  state.reviewCache.set(job.jobId, review);

  if (!state.selectedWellName || !review.wells.some(well => well.name === state.selectedWellName)) {
    state.selectedWellName = review.wells[0]?.name || null;
    state.selectedPhase = review.wells[0]?.availablePhases[0] || null;
  }
}

function buildReviewModel(job, parameterRows, inputRows) {
  const groupedInput = groupInputRowsByWell(inputRows);
  const groupedParams = groupBy(parameterRows, row => row.wellName);
  const chartArtifacts = (job.artifacts || []).filter(artifact => artifact.contentType === "image/png");
  const wells = [];

  for (const [wellName, rows] of Object.entries(groupedParams)) {
    const inputGroup = groupedInput[wellName] || createEmptyInputGroup();
    const byMetric = groupBy(rows, row => row.metric);
    const availablePhases = Object.keys(byMetric).filter(metric => ["Oil", "Gas", "Production"].includes(metric));
    const chartByMetric = {};
    for (const metric of [...availablePhases, "Water"]) {
      const chartName = `${sanitizeFileStem(`${wellName}_${metric}`)}_forecast.png`;
      const artifact = chartArtifacts.find(item => item.name === chartName);
      if (artifact) {
        chartByMetric[metric] = artifact;
      }
    }

    const primaryRowsByMetric = {};
    for (const metric of ["Oil", "Gas", "Production", "WOR"]) {
      primaryRowsByMetric[metric] = selectPrimaryRow(byMetric[metric] || []);
    }

    wells.push({
      name: wellName,
      parameterRows: rows,
      inputGroup,
      availablePhases,
      chartByMetric,
      primaryRowsByMetric,
      observedCum: {
        Oil: inputGroup.observedCum.Oil,
        Gas: inputGroup.observedCum.Gas,
        Water: inputGroup.observedCum.Water
      },
      unitsByMetric: {
        Oil: "BOPD",
        Gas: "MCFD",
        Water: "BWPD"
      },
      cadence: rows[0]?.cadence || "Daily",
      pressureUsed: rows.some(row => row.pressureUsed),
      lateralLength: inputGroup.lateralLength,
      sourcePointCount: inputGroup.pointCount,
      lastTime: inputGroup.lastTime
    });
  }

  wells.sort((a, b) => a.name.localeCompare(b.name));
  return { job, parameterRows, inputRows, wells };
}

function groupInputRowsByWell(rows) {
  const groups = {};
  for (const row of rows) {
    const wellName = firstValue(row, ["WellName", "Well", "Name"]);
    const time = toNumber(firstValue(row, ["Time", "Day", "Month"]));
    if (!wellName || !isFiniteNumber(time)) continue;

    if (!groups[wellName]) {
      groups[wellName] = createEmptyInputGroup();
    }

    groups[wellName].rows.push({
      time,
      oil: toNumber(firstValue(row, ["Oil", "OilRate", "GrossOil"])),
      gas: toNumber(firstValue(row, ["Gas", "GasRate", "GrossGas"])),
      water: toNumber(firstValue(row, ["Water", "WaterRate", "GrossWater"])),
      production: toNumber(firstValue(row, ["Production", "Rate"])),
      lateralLength: toNumber(firstValue(row, ["LateralLength", "PerfLateralLength", "CompletedLateralLength"]))
    });
  }

  for (const group of Object.values(groups)) {
    group.rows.sort((a, b) => a.time - b.time);
    group.pointCount = group.rows.length;
    group.lastTime = group.rows[group.rows.length - 1]?.time ?? null;
    group.lateralLength = group.rows.find(row => isFiniteNumber(row.lateralLength))?.lateralLength ?? null;
    group.observedCum = {
      Oil: integrateSeries(group.rows, "oil"),
      Gas: integrateSeries(group.rows, "gas"),
      Water: integrateSeries(group.rows, "water"),
      Production: integrateSeries(group.rows, "production")
    };
  }

  return groups;
}

function createEmptyInputGroup() {
  return {
    rows: [],
    pointCount: 0,
    lastTime: null,
    lateralLength: null,
    observedCum: { Oil: null, Gas: null, Water: null, Production: null }
  };
}

function integrateSeries(rows, key) {
  if (!rows || rows.length < 2) return null;
  let cumulative = 0;
  for (let index = 1; index < rows.length; index += 1) {
    const current = rows[index][key];
    const dt = Math.max(0, rows[index].time - rows[index - 1].time);
    if (isFiniteNumber(current)) {
      cumulative += current * dt;
    }
  }
  return cumulative;
}

function selectPrimaryRow(rows) {
  if (!rows || rows.length === 0) return null;
  const order = [
    "Prophet-Like ARPS",
    "Hybrid ARPS",
    "Export ARPS b=1.0",
    "Export ARPS b=0.5",
    "Export ARPS b=1.5",
    "TrailingAverage"
  ];

  const picked = [...rows].sort((left, right) => {
    const leftIndex = order.indexOf(left.modelType);
    const rightIndex = order.indexOf(right.modelType);
    const leftRank = leftIndex < 0 ? 999 : leftIndex;
    const rightRank = rightIndex < 0 ? 999 : rightIndex;
    return leftRank - rightRank;
  })[0];

  return {
    ...picked,
    qi: toNumber(picked.qi),
    di: toNumber(picked.di),
    b: toNumber(picked.b),
    annualDiPercent: toNumber(picked.annualDiPercent),
    effectiveDate: toNumber(picked.effectiveDate),
    forecastAnchorValue: toNumber(picked.forecastAnchorValue),
    latestObservedValue: toNumber(picked.latestObservedValue)
  };
}

function estimateTotalEur(row, observedCum, lastTime, cadence, reviewState) {
  const remaining = estimateRemaining(row, lastTime, cadence, reviewState);
  if (!isFiniteNumber(remaining)) return null;
  return (observedCum || 0) + remaining;
}

function estimateRemaining(row, lastTime, cadence, reviewState) {
  if (!row || !isFiniteNumber(row.qi) || !isFiniteNumber(row.di) || !isFiniteNumber(row.effectiveDate)) {
    return null;
  }

  const fitShift = Number(reviewState.fitStartShift || 0);
  const effectiveDate = row.effectiveDate + fitShift;
  const di = Math.max(1e-6, row.di * (1 + Number(reviewState.diBias || 0)));
  const b = Math.max(0.05, (row.b ?? 0.5) + Number(reviewState.bBias || 0));
  const dMin = cadence === "Monthly" ? 0.0005 * 30.4375 : 0.0005;
  const endTime = cadence === "Monthly" ? (lastTime || effectiveDate) + 600 : (lastTime || effectiveDate) + 365 * 30;

  function forecastAt(time) {
    if (time < effectiveDate) return 0;
    if (b <= 0.05) {
      return Math.max(0, row.qi * Math.exp(-di * (time - effectiveDate)));
    }

    if (di <= dMin) {
      const denominator = 1 + b * di * (time - effectiveDate);
      return denominator <= 0 ? 0 : Math.max(0, row.qi / Math.pow(denominator, 1 / b));
    }

    const tSwitch = effectiveDate + Math.max(0, (di / dMin - 1) / (b * di));
    if (time <= tSwitch) {
      const denominator = 1 + b * di * (time - effectiveDate);
      return denominator <= 0 ? 0 : Math.max(0, row.qi / Math.pow(denominator, 1 / b));
    }

    const denominator = 1 + b * di * (tSwitch - effectiveDate);
    if (denominator <= 0) return 0;
    const qSwitch = row.qi / Math.pow(denominator, 1 / b);
    return Math.max(0, qSwitch * Math.exp(-dMin * (time - tSwitch)));
  }

  const start = Math.max(lastTime || effectiveDate, effectiveDate);
  let total = 0;
  let previousTime = start;
  let previousRate = forecastAt(start);
  const step = 1;

  for (let time = start + step; time <= endTime; time += step) {
    const rate = forecastAt(time);
    total += ((previousRate + rate) / 2) * (time - previousTime);
    if (rate < 0.01 && previousRate < 0.01) {
      break;
    }
    previousTime = time;
    previousRate = rate;
  }

  return total;
}

function getEffectiveQc(jobId, well) {
  const saved = getStoredReview(jobId, well.name);
  return saved.qc || deriveHeuristicQc(well);
}

function deriveHeuristicQc(well) {
  const hasOil = !!well.primaryRowsByMetric.Oil;
  const hasGas = !!well.primaryRowsByMetric.Gas;
  const hasLegacy = !!well.primaryRowsByMetric.Production;
  const phaseCount = [hasOil, hasGas, hasLegacy].filter(Boolean).length;

  if (phaseCount === 0) return "red";
  if (phaseCount >= 2) return "green";
  return "yellow";
}

function getStoredReview(jobId, wellName) {
  const reviewMap = loadReviewMap(jobId);
  return reviewMap[wellName] || {};
}

function loadReviewMap(jobId) {
  const key = reviewStorageKey(jobId);
  try {
    return JSON.parse(window.localStorage.getItem(key) || "{}");
  } catch {
    return {};
  }
}

function persistReviewMap(jobId, value) {
  window.localStorage.setItem(reviewStorageKey(jobId), JSON.stringify(value));
}

function reviewStorageKey(jobId) {
  return `forecast-review:${jobId}`;
}

function getCurrentWell() {
  const review = state.reviewCache.get(state.selectedJobId);
  if (!review) return null;
  return review.wells.find(well => well.name === state.selectedWellName) || null;
}

async function submitUploadJob() {
  if (!fileInput.files || fileInput.files.length === 0) {
    throw new Error("Choose a CSV file before starting a job.");
  }

  const payload = new FormData();
  payload.append("file", fileInput.files[0]);
  payload.append("phaseMode", phaseModeSelect.value);
  payload.append("generatePdf", String(generatePdfCheckbox.checked));

  return await fetch("/api/forecast-jobs", {
    method: "POST",
    body: payload
  });
}

async function submitLocalPathJob() {
  if (!localCsvPathInput.value.trim()) {
    throw new Error("Enter a local CSV path before starting a job.");
  }

  return await fetch("/api/forecast-jobs/local", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify({
      csvPath: localCsvPathInput.value.trim(),
      phaseMode: Number(phaseModeSelect.value),
      generatePdf: generatePdfCheckbox.checked
    })
  });
}

function findArtifact(job, predicate) {
  return (job.artifacts || []).find(predicate) || null;
}

function formatUtc(value) {
  if (!value) return "Unknown time";
  const date = new Date(value);
  return `${date.toLocaleDateString()} ${date.toLocaleTimeString()}`;
}

function formatMetricValue(value, decimals, suffix) {
  if (!isFiniteNumber(value)) return "n/a";
  const number = Number(value).toLocaleString(undefined, {
    maximumFractionDigits: decimals,
    minimumFractionDigits: decimals
  });
  return suffix ? `${number} ${suffix}` : number;
}

function formatPercent(value) {
  return isFiniteNumber(value) ? `${Number(value).toFixed(1)}%` : "n/a";
}

function clampNumber(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function roundToStep(value, step) {
  return (Math.round(value / step) * step).toFixed(step >= 1 ? 0 : 2);
}

function toNumber(value) {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : null;
}

function isFiniteNumber(value) {
  return typeof value === "number" && Number.isFinite(value);
}

function groupBy(values, keySelector) {
  const grouped = {};
  for (const value of values) {
    const key = keySelector(value);
    if (!grouped[key]) grouped[key] = [];
    grouped[key].push(value);
  }
  return grouped;
}

function sanitizeFileStem(value) {
  return String(value).replace(/[<>:"/\\|?*\u0000-\u001F]/g, "_");
}

function firstValue(row, candidateKeys) {
  for (const key of candidateKeys) {
    if (row[key] !== undefined) return row[key];
  }

  const normalized = Object.keys(row).reduce((acc, current) => {
    acc[current.replace(/\s+/g, "").toLowerCase()] = row[current];
    return acc;
  }, {});
  for (const key of candidateKeys) {
    const normalizedKey = key.replace(/\s+/g, "").toLowerCase();
    if (normalized[normalizedKey] !== undefined) {
      return normalized[normalizedKey];
    }
  }
  return undefined;
}

function parseCsv(text) {
  const rows = [];
  let current = "";
  let row = [];
  let inQuotes = false;

  function pushCell() {
    row.push(current);
    current = "";
  }

  function pushRow() {
    if (row.length > 0 || current.length > 0) {
      pushCell();
      rows.push(row);
      row = [];
    }
  }

  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    const next = text[index + 1];

    if (char === '"') {
      if (inQuotes && next === '"') {
        current += '"';
        index += 1;
      } else {
        inQuotes = !inQuotes;
      }
      continue;
    }

    if (!inQuotes && char === ",") {
      pushCell();
      continue;
    }

    if (!inQuotes && (char === "\n" || char === "\r")) {
      if (char === "\r" && next === "\n") {
        index += 1;
      }
      pushRow();
      continue;
    }

    current += char;
  }
  pushRow();

  if (rows.length === 0) return [];
  const header = rows[0].map(cell => cell.trim());
  return rows.slice(1)
    .filter(values => values.some(value => value.trim() !== ""))
    .map(values => {
      const output = {};
      header.forEach((key, headerIndex) => {
        output[key] = values[headerIndex] ?? "";
      });
      return output;
    });
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
