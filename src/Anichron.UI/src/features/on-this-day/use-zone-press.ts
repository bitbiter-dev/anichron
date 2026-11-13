import { mergeProps } from 'react-aria/mergeProps';
import { useLongPress } from 'react-aria/useLongPress';
import { usePress } from 'react-aria/usePress';
import type { Gestures } from './navigation';
import type { Move } from './story';

export function useZonePress(gestures: Gestures, move: Move) {
  const { pressProps } = usePress({
    onPressStart: gestures.pressStart,
    onPress: gestures.tap(move),
  });
  const { longPressProps } = useLongPress({ onLongPress: gestures.hold });
  return mergeProps(pressProps, longPressProps);
}
