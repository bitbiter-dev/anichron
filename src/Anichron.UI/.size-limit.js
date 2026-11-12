import { readFileSync } from 'node:fs';

const loadedAtStart = [
  ...readFileSync('dist/index.html', 'utf8').matchAll(/(?:src|href)="\/(assets\/[^"]+\.js)"/g),
].map(([, path]) => `dist/${path}`);

export default [
  { name: 'Initial JS', path: loadedAtStart, limit: '205 kB', gzip: true },
  { name: 'CSS', path: 'dist/assets/*.css', limit: '15 kB', gzip: true },
];
