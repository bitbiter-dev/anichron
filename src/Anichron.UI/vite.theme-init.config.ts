import browserslistToEsbuild from 'browserslist-to-esbuild';
import { defineConfig } from 'vite';

export default defineConfig({
  publicDir: false,
  build: {
    target: browserslistToEsbuild(),
    emptyOutDir: false,
    lib: {
      entry: 'src/app/theme-init.ts',
      formats: ['iife'],
      name: 'anichronThemeInit',
      fileName: () => 'theme-init.js',
    },
  },
});
