import { m } from 'motion/react';
import { Button, Dialog, Modal, ModalOverlay } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import type { Memory } from '../../api/flashbacks';
import { Blurhash } from './BlurhashCanvas';
import { CloseButton } from './CloseButton';
import { Failed } from '../../shared/ui/Failed';
import { MemoryActions } from './MemoryActions';
import { ProxyImage } from './ProxyImage';
import { useBurstDetails, useFrameBlurhash } from './use-burst-details';
import type { useInteractions } from './use-interactions';

interface BurstDetailsProps {
  open: boolean;
  card: Memory;
  close: () => void;
  interactions: Pick<ReturnType<typeof useInteractions>, 'toggleFrame' | 'hideFrame'>;
}

export function BurstDetails({ open, ...props }: BurstDetailsProps) {
  return open && <Details {...props} />;
}

function Details({ card, close, interactions }: Omit<BurstDetailsProps, 'open'>) {
  const { t } = useTranslation();
  const { attach, ...details } = useBurstDetails(card, close);
  const { count, frame } = details;
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
              {details.frames.map(({ memory, place }) => (
                <li key={memory.id} className="size-full shrink-0 snap-center snap-always">
                  <Frame memory={memory} label={t('story.frame', { place, count })} />
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
              {details.loaded && t('story.framePlace', { place: details.place, count })}
            </p>
            <div className="pointer-events-auto">
              <CloseButton close={close} />
            </div>
          </div>
          <MemoryActions
            interaction={frame.interaction}
            onToggle={interactions.toggleFrame(frame)}
            onHide={() => {
              interactions.hideFrame(frame);
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

function Frame({ memory, label }: { memory: Memory; label: string }) {
  const { blurhash, onDecoded } = useFrameBlurhash(memory);
  return (
    <div role="img" aria-label={label} className="relative size-full">
      {blurhash && <Blurhash {...blurhash} />}
      <ProxyImage
        src={memory.proxies.preview}
        onDecoded={onDecoded}
        className="relative size-full object-contain"
      />
    </div>
  );
}
