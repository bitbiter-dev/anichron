import type { HTMLAttributes } from 'react';
import { mergeProps } from 'react-aria/mergeProps';
import { useLongPress } from 'react-aria/useLongPress';
import { usePress } from 'react-aria/usePress';
import type { RemovedScreen } from './use-removed';

export function useRow(
  screen: RemovedScreen,
  index: number,
  nameId: string,
): HTMLAttributes<HTMLElement> {
  const { select, drag } = screen;
  const { pressProps } = usePress({
    onPressStart: drag.pressStart,
    onPress: ({ shiftKey }) => {
      screen.press(index, shiftKey);
    },
  });
  const { longPressProps } = useLongPress({
    isDisabled: select.selecting,
    onLongPress: () => {
      drag.hold(index);
    },
  });
  const role = select.selecting
    ? { role: 'checkbox', 'aria-checked': select.isSelected(index), 'aria-labelledby': nameId }
    : { role: 'button' };
  return mergeProps(pressProps, longPressProps, role, {
    tabIndex: 0,
    'data-index': index,
    onKeyDown: screen.selectAll.key,
  });
}
