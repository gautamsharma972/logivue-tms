import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API runs on 5155 in development (see backend launchSettings). Requests to /api are proxied so the browser sees one origin.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5155', changeOrigin: true },
    },
  },
})
