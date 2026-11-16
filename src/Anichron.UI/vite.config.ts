import optimizeLocales from '@react-aria/optimize-locales-plugin';
import babel from '@rolldown/plugin-babel';
import tailwindcss from '@tailwindcss/vite';
import react, { reactCompilerPreset } from '@vitejs/plugin-react';
import browserslistToEsbuild from 'browserslist-to-esbuild';
import { build, type Plugin } from 'vite';
import { VitePWA } from 'vite-plugin-pwa';
import { readdirSync } from 'node:fs';
import { basename } from 'node:path';
import { defineConfig } from 'vitest/config';

function catalogLocales() {
  return readdirSync(new URL('src/i18n/locales', import.meta.url)).map((file) =>
    basename(file, '.json'),
  );
}

async function themeInit() {
  const output = await build({
    configFile: 'vite.theme-init.config.ts',
    logLevel: 'silent',
    build: { write: false },
  });
  const [bundle] = [output].flat();
  return bundle && 'output' in bundle ? bundle.output[0].code : '';
}

function themeInitInDev(): Plugin {
  return {
    name: 'theme-init-in-dev',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use('/theme-init.js', (_request, response, next) => {
        themeInit()
          .then((code) => {
            response.setHeader('Content-Type', 'text/javascript');
            response.end(code);
          })
          .catch(next);
      });
    },
  };
}

function themeInitInBuild(): Plugin {
  return {
    name: 'theme-init-in-build',
    apply: 'build',
    async generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'theme-init.js', source: await themeInit() });
    },
  };
}

export default defineConfig({
  plugins: [
    optimizeLocales.vite({ locales: catalogLocales() }),
    react(),
    babel({ presets: [reactCompilerPreset()] }),
    tailwindcss(),
    themeInitInDev(),
    themeInitInBuild(),
    VitePWA({
      strategies: 'injectManifest',
      srcDir: 'src/app',
      filename: 'sw.ts',
      injectRegister: false,
      manifest: false,
      injectManifest: {
        globPatterns: ['index.html', 'theme-init.js', 'assets/**'],
        rollupFormat: 'iife',
      },
    }),
  ],
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
    isolate: false,
    sequence: { shuffle: true },
    css: { include: [/\/tokens\.css/] },
    reporters: process.env.GITHUB_ACTIONS
      ? ['default', ['github-actions', { jobSummary: { enabled: false } }]]
      : ['default'],
  },
});
