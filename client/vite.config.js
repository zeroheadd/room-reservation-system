import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

/**
 * Kept as plain JavaScript on purpose: Vite bundles a TypeScript config by spawning esbuild as
 * a child process, which is not possible in every restricted build environment. Nothing here
 * benefits from type annotations.
 *
 * The dev server proxies the API to the ASP.NET backend, so the SPA can call /api/... with no
 * CORS involvement and no environment-specific base URL during development.
 */
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true
      }
    }
  },
  build: {
    outDir: 'dist',
    sourcemap: true
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts']
  }
});
