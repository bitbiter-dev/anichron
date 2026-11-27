export interface Selection {
  readonly ids: ReadonlySet<string>;
  readonly anchor: string | null;
}

export function noSelection(): Selection {
  return { ids: new Set(), anchor: null };
}

export function toggled({ ids }: Selection, all: readonly string[], index: number): Selection {
  const id = all[index] as string;
  const next = new Set(ids);
  if (!next.delete(id)) next.add(id);
  return { ids: next, anchor: id };
}

export function ranged(selection: Selection, all: readonly string[], index: number): Selection {
  const { anchor } = selection;
  if (anchor === null) return toggled(selection, all, index);
  return { ...painted(selection, all, all.indexOf(anchor), index, true), anchor };
}

export function painted(
  base: Selection,
  all: readonly string[],
  from: number,
  to: number,
  select: boolean,
): Selection {
  const next = new Set(base.ids);
  for (const id of all.slice(Math.min(from, to), Math.max(from, to) + 1)) {
    if (select) next.add(id);
    else next.delete(id);
  }
  return { ids: next, anchor: all[from] as string };
}

export function allOf(all: readonly string[]): Selection {
  return { ids: new Set(all), anchor: null };
}

export function kept({ ids, anchor }: Selection, all: readonly string[]): Selection {
  const present = new Set(all);
  return {
    ids: new Set([...ids].filter((id) => present.has(id))),
    anchor: all.find((id) => id === anchor) ?? null,
  };
}

export function holdsAll({ ids }: Selection, all: readonly string[]) {
  return all.every((id) => ids.has(id));
}
