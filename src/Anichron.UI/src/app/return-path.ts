const sameOriginPath = /^\/(?![/\\])/;

export function returnPath(redirect: unknown) {
  return typeof redirect === 'string' && sameOriginPath.test(redirect) ? redirect : '/';
}
