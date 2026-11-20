const operations = [
  { event: 'app_started', label: 'App startup', bucket: 'startup_ms' },
  { event: 'fetch_completed', label: 'Fetch messages', bucket: 'duration' },
  { event: 'cluster_connected', label: 'Load topics' },
];

export const eventNames = [
  'app_first_run', 'heartbeat', 'feature_used', ...operations.map(({ event }) => event),
];

const bucketOrder = ['<100ms', '<500ms', '<1s', '<3s', '<10s', '<30s', '<1m', '1m+'];

function percentile(sortedValues, fraction) {
  return sortedValues[Math.ceil(sortedValues.length * fraction) - 1] ?? null;
}

function duration(value) {
  if (typeof value !== 'number' && (typeof value !== 'string' || !/^\d+(\.0+)?$/.test(value))) {
    return null;
  }
  const milliseconds = Number(value);
  return Number.isSafeInteger(milliseconds) && milliseconds >= 0 ? milliseconds : null;
}

export function summarize(events, installations) {
  const latestByInstall = new Map();
  const features = new Map();
  const byEvent = new Map(eventNames.map(name => [name, []]));
  const unknownSessions = new Set();

  for (const event of events) {
    byEvent.get(event.name).push(event.properties);
    const install = installations.get(event.sessionId);
    if (install) {
      const previous = latestByInstall.get(install);
      if (!previous || event.createdAt > previous.createdAt ||
          (event.createdAt === previous.createdAt && event.id > previous.id)) {
        latestByInstall.set(install, event);
      }
    } else {
      unknownSessions.add(event.sessionId);
    }

    const feature = event.properties.feature;
    if (event.name === 'feature_used' && feature) {
      if (!features.has(feature)) features.set(feature, { uses: 0, installs: new Set() });
      const usage = features.get(feature);
      usage.uses++;
      if (install) usage.installs.add(install);
    }
  }

  const versionCounts = new Map();
  for (const { properties } of latestByInstall.values()) {
    const version = properties.app_version || 'Unknown';
    versionCounts.set(version, (versionCounts.get(version) ?? 0) + 1);
  }

  const activeInstallations = latestByInstall.size;
  const share = count => activeInstallations ? count / activeInstallations : null;
  return {
    activeInstallations,
    launches: byEvent.get('app_started').length,
    featureEvents: byEvent.get('feature_used').length,
    unknownSessions: unknownSessions.size,
    versions: Array.from(versionCounts, ([version, installations]) => ({
      version, installations, share: share(installations),
    })).sort((a, b) => b.installations - a.installations || a.version.localeCompare(b.version)),
    features: Array.from(features, ([feature, { uses, installs }]) => ({
      feature, uses, installations: installs.size, reach: share(installs.size),
    })).sort((a, b) => b.uses - a.uses || a.feature.localeCompare(b.feature)),
    latency: operations.map(({ event, label, bucket }) => {
      const samples = byEvent.get(event);
      const durations = [];
      const counts = new Map();
      for (const properties of samples) {
        const milliseconds = duration(properties.duration_ms);
        if (milliseconds !== null) {
          durations.push(milliseconds);
        } else if (bucketOrder.includes(properties[bucket])) {
          const range = properties[bucket];
          counts.set(range, (counts.get(range) ?? 0) + 1);
        }
      }

      durations.sort((a, b) => a - b);
      const buckets = bucketOrder.filter(range => counts.has(range))
        .map(range => ({ range, count: counts.get(range) }));
      return {
        operation: label,
        samples: durations.length,
        p50: percentile(durations, 0.50),
        p95: percentile(durations, 0.95),
        buckets,
        untimed: samples.length - durations.length - buckets.reduce((sum, entry) => sum + entry.count, 0),
      };
    }),
  };
}
