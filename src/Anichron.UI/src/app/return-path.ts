const sameOriginPath = /^\/(?![/\\])/;

export function returnPath({ redirect }: Record<string, unknown>) {
  return typeof redirect === 'string' && sameOriginPath.test(redirect) ? redirect : '/';
}
