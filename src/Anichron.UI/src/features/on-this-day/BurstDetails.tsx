import { Dialog, Button, Modal, ModalOverlay } from 'react-aria-components';
import { m } from 'motion/react';
import { useTranslation } from 'react-i18next';
import type { Memory } from '../../api/flashbacks';
import { CloseButton } from './CloseButton';
import { MemoryActions } from './MemoryActions';
import { Failed } from './Failed';
import { ProxyImage } from './ProxyImage';
import { useBurstDetails } from './use-burst-details';
import type { Toggle } from './use-interactions';

interface BurstDetailsProps {
  open: boolean;
  card: Memory;
  close: () => void;
  interactions: {
    toggle: (flag: Toggle, memory: Memory) => (on: boolean) => void;
    hide: (memory: Memory) => void;
  };
}

export function BurstDetails({ open, ...props }: BurstDetailsProps) {
  return open && <Details {...props} />;
}

function Details({ card, close, interactions }: Omit<BurstDetailsProps, 'open'>) {
  const { t } = useTranslation();
  const { attach, ...details } = useBurstDetails(card, close);
  const { frames, current } = details;
  const count = frames.length;
  const frame = frames[current] as Memory;
  return (
    <ModalOverlay isOpen onOpenChange={close} className="fixed inset-0 z-[25] bg-player">
      <Modal className="size-full">
        <Dialog
          aria-label={t('story.frames')}
          className="relative size-full text-on-player outline-none"
        >
          <m.div
            drag="y"
            dragConstraints={{ top: 0, bottom: 0 }}
            dragSnapToOrigin
            onDragStart={details.onDragStart}
            onDragEnd={details.onDragEnd}
            className="absolute inset-0 select-none [-webkit-touch-callout:none]"
          >
            <ul
              ref={attach}
              onScroll={details.onScroll}
              {...details.pressProps}
              className="flex size-full snap-x snap-mandatory [scrollbar-width:none] overflow-x-auto overscroll-x-contain"
            >
              {frames.map((frame, index) => (
                <li key={frame.id} className="size-full shrink-0 snap-center snap-always">
                  <div
                    role="img"
                    aria-label={t('story.frame', { place: index + 1, count })}
                    className="size-full"
                  >
                    <ProxyImage src={frame.proxies.preview} className="size-full object-contain" />
                  </div>
                </li>
              ))}
            </ul>
          </m.div>
          <Button onPress={details.moveBy(-1)} className="sr-only">
            {t('story.previousFrame')}
          </Button>
          <Button onPress={details.moveBy(1)} className="sr-only">
            {t('story.nextFrame')}
          </Button>
          <div className="pointer-events-none absolute inset-x-0 top-0 flex items-center bg-linear-to-b from-player/60 px-2 pt-[calc(env(safe-area-inset-top)+0.5rem)] pb-6">
            <p className="flex-1 text-small font-semibold">
              {details.loaded && t('story.framePlace', { place: current + 1, count })}
            </p>
            <div className="pointer-events-auto">
              <CloseButton close={close} />
            </div>
          </div>
          <MemoryActions
            interaction={frame.interaction}
            onToggle={(flag) => interactions.toggle(flag, frame)}
            onHide={() => {
              interactions.hide(frame);
            }}
          />
          {details.failed && (
            <div className="absolute inset-x-4 top-1/2 -translate-y-1/2">
              <Failed message="story.framesFailed" onRetry={details.retry} />
            </div>
          )}
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
