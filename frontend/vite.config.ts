import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Proxy evita CORS em dev: o painel roda no Vite (5173) e a API no 5039.
// Em produção o dist/ é servido pela própria API (mesma origem).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      '/v1': { target: 'http://localhost:5039', changeOrigin: true },
    },
  },
})
