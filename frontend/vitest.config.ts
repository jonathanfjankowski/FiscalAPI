import { defineConfig } from 'vitest/config'
import { fileURLToPath } from 'node:url'

// Suite só para lógica pura de `src/lib/` (decisão 7 do plano): sem DOM,
// sem React Testing Library. `environment: 'node'` porque nada testado
// toca em `window`/`document` — `copiar()` é o único ponto sensível e
// trata o caso "navigator indisponível" sem mock.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
