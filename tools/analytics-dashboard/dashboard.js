const byId = id => document.getElementById(id);
const range = byId('date-range');
const refresh = byId('refresh');
const report = byId('report');
const status = byId('status');
const integer = new Intl.NumberFormat();
const percent = new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 1 });
const date = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short', timeZone: 'UTC' });
const formatPercent = value => value === null ? '—' : percent.format(value);
const formatDuration = value => value === null ? '—' : `${integer.format(value)} ms`;
const featureLabels = { tab_open: 'Open tab', tab_close: 'Close tab', app_log_toggle: 'App log toggle' };

function featureLabel(feature) {
  if (Object.hasOwn(featureLabels, feature)) return featureLabels[feature];
  return feature.charAt(0).toUpperCase() + feature.slice(1).replaceAll('_', ' ');
}

function shareCell(value) {
  const bar = document.createElement('progress');
  bar.value = value;
  bar.max = 1;
  bar.setAttribute('aria-hidden', 'true');
  const wrapper = document.createElement('span');
  wrapper.className = 'share';
  wrapper.append(formatPercent(value), bar);
  return wrapper;
}

function renderTable(id, rows, columns, emptyText) {
  const body = byId(id);
  const content = document.createDocumentFragment();
  for (const cells of rows) {
    const row = content.appendChild(document.createElement('tr'));
    cells.forEach((value, index) => {
      const cell = row.appendChild(document.createElement(index === 0 ? 'th' : 'td'));
      if (index === 0) cell.scope = 'row';
      cell.append(value);
    });
  }
  if (!rows.length) {
    const cell = content.appendChild(document.createElement('tr')).insertCell();
    cell.colSpan = columns;
    cell.className = 'empty';
    cell.textContent = emptyText;
  }
  body.replaceChildren(content);
}

function historicalTiming({ samples, buckets, untimed }) {
  const parts = buckets.map(({ range, count }) => `${range} (${integer.format(count)})`);
  if (untimed) parts.push(`${integer.format(untimed)} without timing`);
  return parts.join(' · ') || (samples ? '—' : 'No events');
}

function render(data) {
  byId('active-installations').textContent = integer.format(data.activeInstallations);
  byId('launches').textContent = integer.format(data.launches);
  byId('feature-events').textContent = integer.format(data.featureEvents);
  renderTable('versions', data.versions.map(row => [
    row.version, integer.format(row.installations), shareCell(row.share),
  ]), 3, 'No installations in this range.');
  renderTable('features', data.features.map(row => [
    featureLabel(row.feature), integer.format(row.uses), integer.format(row.installations), formatPercent(row.reach),
  ]), 4, 'No feature events in this range.');
  renderTable('latency', data.latency.map(row => [
    row.operation, integer.format(row.samples), formatDuration(row.p50), formatDuration(row.p95), historicalTiming(row),
  ]), 5);
  byId('report-period').textContent = `${date.formatRange(new Date(data.startAt), new Date(data.endAt))} UTC`;
  const identityNote = byId('identity-note');
  const count = data.unknownSessions;
  identityNote.hidden = count === 0;
  identityNote.textContent = `Installation counts exclude ${integer.format(count)} Umami ${count === 1 ? 'session' : 'sessions'} without a single installation ID.`;
  report.hidden = false;
}

async function load() {
  range.disabled = refresh.disabled = true;
  report.hidden = true;
  status.hidden = false;
  status.classList.remove('error');
  status.textContent = 'Loading report…';
  try {
    const response = await fetch(`/api/report?days=${encodeURIComponent(range.value)}`);
    if (!response.ok) throw new Error(await response.text());
    render(await response.json());
    status.textContent = '';
    status.hidden = true;
  } catch (error) {
    status.textContent = error.message || 'Unable to load the report.';
    status.classList.add('error');
  } finally {
    range.disabled = refresh.disabled = false;
  }
}

range.addEventListener('change', load);
refresh.addEventListener('click', load);
load();
