import optimizeLocales from '@react-aria/optimize-locales-plugin';
import babel from '@rolldown/plugin-babel';
import tailwindcss from '@tailwindcss/vite';
import react, { reactCompilerPreset } from '@vitejs/plugin-react';
import browserslistToEsbuild from 'browserslist-to-esbuild';
import { build, type Plugin } from 'vite';
import { readdirSync } from 'node:fs';
import { basename } from 'node:path';
import { defineConfig } from 'vitest/config';

function catalogLocales() {
  return readdirSync(new URL('src/i18n/locales', import.meta.url)).map((file) =>
    basename(file, '.json'),
  );
}

function themeInitInDev(): Plugin {
  return {
    name: 'theme-init-in-dev',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use('/theme-init.js', (_request, response, next) => {
        build({
          configFile: 'vite.theme-init.config.ts',
          logLevel: 'silent',
          build: { write: false },
        })
          .then((output) => {
            const [bundle] = [output].flat();
            const chunk = bundle && 'output' in bundle ? bundle.output[0] : undefined;
            response.setHeader('Content-Type', 'text/javascript');
            response.end(chunk?.code);
          })
          .catch(next);
      });
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
    env: { TZ: 'Pacific/Auckland' },
    css: { include: [/\/tokens\.css/] },
    reporters: process.env.GITHUB_ACTIONS
      ? ['default', ['github-actions', { jobSummary: { enabled: false } }]]
      : ['default'],
  },
});
