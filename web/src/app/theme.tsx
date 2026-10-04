import { ConfigProvider, theme as antdTheme, type ThemeConfig } from 'antd'
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'

export type ThemeMode = 'light' | 'dark'

const STORAGE_KEY = 'tms.theme'

function initialMode(): ThemeMode {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'light' || saved === 'dark') return saved
  } catch {
    /* fall through to system preference */
  }
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

function buildTheme(mode: ThemeMode): ThemeConfig {
  const dark = mode === 'dark'
  return {
    algorithm: dark ? antdTheme.darkAlgorithm : antdTheme.defaultAlgorithm,
    token: {
      colorPrimary: '#2f5bea',
      colorInfo: '#2f5bea',
      colorSuccess: '#1f9d63',
      colorWarning: '#d98a0b',
      colorError: '#d6403f',
      borderRadius: 6,
      fontFamily:
        "Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
      fontSize: 14,
      colorBgLayout: dark ? '#0d1117' : '#f4f6fa',
    },
    components: {
      Layout: {
        siderBg: dark ? '#0a0e14' : '#0f1b33',
        headerBg: dark ? '#141922' : '#ffffff',
        headerPadding: '0 20px',
        headerHeight: 56,
      },
      Menu: {
        darkItemBg: 'transparent',
        darkSubMenuItemBg: 'transparent',
        darkItemSelectedBg: 'rgba(47, 91, 234, 0.35)',
        itemBorderRadius: 6,
      },
      Table: { headerBg: dark ? '#1a2029' : '#f7f8fb', headerColor: dark ? '#c9d1d9' : '#3b4558' },
      Card: { headerFontSize: 15 },
    },
  }
}

interface ThemeContextValue {
  mode: ThemeMode
  toggle: () => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

export function AppThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<ThemeMode>(initialMode)

  useEffect(() => {
    document.documentElement.dataset.theme = mode
    document.documentElement.style.colorScheme = mode
    try {
      localStorage.setItem(STORAGE_KEY, mode)
    } catch {
      /* preference just won't persist */
    }
  }, [mode])

  const toggle = useCallback(() => setMode((m) => (m === 'dark' ? 'light' : 'dark')), [])
  const value = useMemo(() => ({ mode, toggle }), [mode, toggle])
  const config = useMemo(() => buildTheme(mode), [mode])

  return (
    <ThemeContext.Provider value={value}>
      <ConfigProvider theme={config} componentSize="middle">
        {children}
      </ConfigProvider>
    </ThemeContext.Provider>
  )
}

export function useThemeMode(): ThemeContextValue {
  const ctx = useContext(ThemeContext)
  if (!ctx) throw new Error('useThemeMode must be used inside AppThemeProvider')
  return ctx
}
