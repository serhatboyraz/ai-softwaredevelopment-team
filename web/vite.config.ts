import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5284',
      '/health': 'http://localhost:5284',
      '/workflowHub': {
        target: 'http://localhost:5284',
        ws: true,
      },
    },
  },
})
