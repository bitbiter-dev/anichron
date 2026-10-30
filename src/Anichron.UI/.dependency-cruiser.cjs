const testCode = ['^src/test/', '\\.test\\.tsx?$'];

/** @type {import('dependency-cruiser').IConfiguration} */
module.exports = {
  forbidden: [
    {
      name: 'features-are-isolated',
      comment: 'features/X never imports features/Y.',
      severity: 'error',
      from: { path: '^src/features/([^/]+)/' },
      to: { path: '^src/features/', pathNot: '^src/features/$1/' },
    },
    {
      name: 'shared-and-api-stay-below-features',
      comment: 'shared/ and api/ never import features/ or app/.',
      severity: 'error',
      from: { path: '^src/(shared|api)/' },
      to: { path: '^src/(features|app)/' },
    },
    {
      name: 'only-app-imports-features',
      comment: 'Only app/ imports a feature; a feature imports only itself.',
      severity: 'error',
      from: { pathNot: '^src/(app|features)/' },
      to: { path: '^src/features/' },
    },
    {
      name: 'no-circular',
      comment: 'No import cycles.',
      severity: 'error',
      from: {},
      to: { circular: true },
    },
    {
      name: 'production-imports-no-test-code',
      comment: 'Production code never imports test code.',
      severity: 'error',
      from: { path: '^src/', pathNot: testCode },
      to: { path: testCode },
    },
    {
      name: 'logic-imports-no-views',
      comment: 'A .ts file never imports a .tsx file, so logic stays out of views (ADR 0014).',
      severity: 'error',
      from: { path: '\\.ts$', pathNot: testCode },
      to: { path: '\\.tsx$' },
    },
  ],
  options: {
    doNotFollow: { path: 'node_modules' },
    tsPreCompilationDeps: true,
    tsConfig: { fileName: 'tsconfig.app.json' },
    enhancedResolveOptions: {
      exportsFields: ['exports'],
      conditionNames: ['import', 'require', 'node', 'default', 'types'],
      extensions: ['.ts', '.tsx', '.js', '.json'],
    },
  },
};
