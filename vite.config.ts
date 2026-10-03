import { defineConfig } from 'vitest/config';

// Relative base so the build works both at the domain root and under
// https://<user>.github.io/tinkerstoybox/ on GitHub Pages.
export default defineConfig({
  base: './',
  build: {
    target: 'es2022',
    outDir: 'dist',
    chunkSizeWarningLimit: 3000,
  },
  test: {
    environment: 'node',
    include: ['tests/**/*.test.ts'],
    testTimeout: 60_000,
  },
});
