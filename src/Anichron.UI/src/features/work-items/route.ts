export function workItemsKey() {
  return ['work-items'] as const;
}

export function listSearch(search: Record<string, unknown>): { page?: number } {
  return {
    page: Number(search.page) || undefined,
  };
}
