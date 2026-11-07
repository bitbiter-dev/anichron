import babel from '@rolldown/plugin-babel';
import tailwindcss from '@tailwindcss/vite';
import react, { reactCompilerPreset } from '@vitejs/plugin-react';
import browserslistToEsbuild from 'browserslist-to-esbuild';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [react(), babel({ presets: [reactCompilerPreset()] }), tailwindcss()],
  build: {
    target: browserslistToEsbuild(),
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5207',
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    restoreMocks: true,
    css: { include: [/\/tokens\.css/] },
  },
});
