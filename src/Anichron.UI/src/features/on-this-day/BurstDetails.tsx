import { useTranslation } from 'react-i18next';
import type { Memory } from '../../api/flashbacks';
import { Failed } from '../../shared/ui/Failed';
import { Frames } from '../../shared/ui/Frames';
import { MemoryImage } from '../../shared/ui/MemoryImage';
import { MemoryActions } from './MemoryActions';
import { useBlurhash } from '../../shared/use-blurhash';
import { useBurstDetails } from './use-burst-details';
import type { useInteractions } from './use-interactions';

interface BurstDetailsProps {
  open: boolean;
  cover: Memory;
  close: () => void;
  interactions: Pick<ReturnType<typeof useInteractions>, 'toggleFrame' | 'hideFrame'>;
}

export function BurstDetails({ open, ...props }: BurstDetailsProps) {
  return open && <Details {...props} />;
}

function Details({ cover, close, interactions }: Omit<BurstDetailsProps, 'open'>) {
  const { t } = useTranslation();
  const { viewer, count, frame, ...details } = useBurstDetails(cover, close);
  return (
    <Frames
      label={t('story.frames')}
      close={close}
      viewer={viewer}
      slides={details.frames.map(({ memory, place, src }) => ({
        key: memory.id,
        slide: <Frame memory={memory} src={src} label={t('story.frame', { place, count })} />,
      }))}
      moves={{ previous: t('story.previousFrame'), next: t('story.nextFrame') }}
      title={details.loaded && t('story.framePlace', { place: viewer.place, count })}
    >
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
    </Frames>
  );
}

interface FrameProps {
  memory: Memory;
  src: string | null;
  label: string;
}

function Frame({ memory, src, label }: FrameProps) {
  const { blurhash, onDecoded } = useBlurhash(memory);
  return (
    <MemoryImage
      label={label}
      blurhash={blurhash}
      src={src}
      onDecoded={onDecoded}
      className="relative size-full"
    />
  );
}
