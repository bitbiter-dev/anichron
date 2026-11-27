import { type KeyboardEvent, useState } from 'react';
import {
  allOf,
  holdsAll,
  kept,
  noSelection,
  painted,
  ranged,
  type Selection,
  toggled,
} from './selection';

interface Painting {
  selection: Selection;
  select: boolean;
}

export function useSelect(ids: readonly string[]) {
  const [selecting, setSelecting] = useState(false);
  const [stored, setStored] = useState(noSelection);
  const selection = kept(stored, ids);

  return {
    selecting,
    count: selection.ids.size,
    holdsAll: holdsAll(selection, ids),
    selected: [...selection.ids],
    isSelected: (index: number) => selection.ids.has(ids[index] as string),
    start: () => {
      setSelecting(true);
    },
    cancel: () => {
      setSelecting(false);
      setStored(noSelection());
    },
    toggle: (index: number, range: boolean) => {
      setStored(range ? ranged(selection, ids, index) : toggled(selection, ids, index));
    },
    selectAll: (all: readonly string[] = ids) => {
      setStored(allOf(all));
    },
    clear: () => {
      setStored(noSelection());
    },
    selectAllKey: (event: KeyboardEvent) => {
      if (!selecting || event.key !== 'a' || !(event.ctrlKey || event.metaKey)) return;
      event.preventDefault();
      setStored(allOf(ids));
    },
    begin: (index: number): Painting => ({
      selection,
      select: !selection.ids.has(ids[index] as string),
    }),
    hold: (index: number): Painting => {
      const held = toggled(noSelection(), ids, index);
      setSelecting(true);
      setStored(held);
      return { selection: held, select: true };
    },
    paint: ({ selection: from, select }: Painting, start: number, end: number) => {
      setStored(painted(from, ids, start, end, select));
    },
  };
}
