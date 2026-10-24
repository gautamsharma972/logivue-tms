import { useSyncExternalStore } from 'react'

const query = '(max-width: 767px)'

/** True on phones and narrow windows. Follows the screen as it rotates or resizes. */
export function useIsMobile(): boolean {
  return useSyncExternalStore(
    (notify) => {
      const media = window.matchMedia(query)
      media.addEventListener('change', notify)
      return () => media.removeEventListener('change', notify)
    },
    () => window.matchMedia(query).matches,
    () => false,
  )
}
