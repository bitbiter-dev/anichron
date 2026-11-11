export function returnPath({ redirect }: Record<string, unknown>) {
  return typeof redirect === 'string' && /^\/(?![/\\])/.test(redirect) ? redirect : '/';
}
