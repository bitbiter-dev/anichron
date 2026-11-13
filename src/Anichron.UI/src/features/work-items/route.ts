export const workItemsKey = ['work-items'] as const;

export const listSearch = (search: Record<string, unknown>): { page?: number } => ({
  page: Number(search.page) || undefined,
});
