import { mergeProps } from 'react-aria/mergeProps';
import { useLongPress } from 'react-aria/useLongPress';
import { usePress } from 'react-aria/usePress';
import type { Gestures } from './navigation';
import type { Tap } from './story';

export function useZonePress(gestures: Gestures, move: Tap) {
  const { pressProps } = usePress({
    onPressStart: gestures.pressStart,
    onPress: gestures.tap(move),
  });
  const { longPressProps } = useLongPress({ onLongPress: gestures.hold });
  return mergeProps(pressProps, longPressProps);
}
