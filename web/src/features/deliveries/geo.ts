import type { GeoDto } from '@/lib/api/types'

/** Where the phone is now, or nothing if it cannot tell in time. Never blocks the driver: the server decides whether a missing fix matters. */
export function currentFix(timeoutMs = 8000): Promise<GeoDto> {
  const none: GeoDto = { latitude: null, longitude: null, accuracyM: null }
  if (typeof navigator === 'undefined' || !navigator.geolocation) return Promise.resolve(none)
  return new Promise((resolve) => {
    navigator.geolocation.getCurrentPosition(
      (p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude, accuracyM: p.coords.accuracy }),
      () => resolve(none),
      { enableHighAccuracy: true, timeout: timeoutMs, maximumAge: 15_000 },
    )
  })
}
