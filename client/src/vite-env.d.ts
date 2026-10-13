/// <reference types="vite/client" />

/**
 * Environment variables this client understands.
 * VITE_API_BASE_URL is optional: during development Vite proxies /api to the backend, and in
 * production the SPA is normally served by the API itself, so an empty base URL works.
 */
interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
