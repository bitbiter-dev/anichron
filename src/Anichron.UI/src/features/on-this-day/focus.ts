export function focusOnMount(element: HTMLElement | null) {
  (element as HTMLElement).focus();
  return () => undefined;
}
