import { m } from 'motion/react';
import type { ReactNode } from 'react';
import { Button } from 'react-aria-components';
import type { FrameViewer } from '../use-frames';
import { CloseButton } from './CloseButton';
import { PlayerDialog } from './PlayerDialog';

interface FramesProps {
  label: string;
  close: () => void;
  viewer: FrameViewer;
  slides: readonly { key: string; slide: ReactNode }[];
  moves: { previous: string; next: string };
  title: ReactNode;
  children: ReactNode;
}

export function Frames({ label, close, viewer, slides, moves, title, children }: FramesProps) {
  const { attach, ...controls } = viewer;
  return (
    <PlayerDialog label={label} layer="frames" close={close}>
      <m.div
        drag="y"
        dragConstraints={{ top: 0, bottom: 0 }}
        dragSnapToOrigin
        onDragStart={controls.onDragStart}
        onDragEnd={controls.onDragEnd}
        className="absolute inset-0 select-none [-webkit-touch-callout:none]"
      >
        <ul
          ref={attach}
          onScroll={controls.onScroll}
          {...controls.pressProps}
          className="flex size-full snap-x snap-mandatory [scrollbar-width:none] overflow-x-auto overscroll-x-contain"
        >
          {slides.map(({ key, slide }) => (
            <li key={key} className="size-full shrink-0 snap-center snap-always">
              {slide}
            </li>
          ))}
        </ul>
      </m.div>
      <Button onPress={controls.moveBy(-1)} className="sr-only">
        {moves.previous}
      </Button>
      <Button onPress={controls.moveBy(1)} className="sr-only">
        {moves.next}
      </Button>
      <div className="pointer-events-none absolute inset-x-0 top-0 flex items-center bg-linear-to-b from-player/60 px-2 pt-[calc(env(safe-area-inset-top)+0.5rem)] pb-6">
        <p className="flex-1 text-small font-semibold">{title}</p>
        <div className="pointer-events-auto">
          <CloseButton close={close} />
        </div>
      </div>
      {children}
    </PlayerDialog>
  );
}
