export function consumeRadarNotifications<T extends { documentAnalysisId: string }>(items: readonly T[], notified: Set<string>): T | null {
  // The API orders documents newest first; consume the batch without queuing older items.
  const latest = items[0]
  const next = latest && !notified.has(latest.documentAnalysisId) ? latest : null
  for (const item of items) notified.add(item.documentAnalysisId)
  return next
}
