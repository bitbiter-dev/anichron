import { Eye } from 'lucide-react';
import { domMax, LazyMotion, MotionConfig } from 'motion/react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { Frames } from '../../shared/ui/Frames';
import { SlideView } from '../../shared/ui/SlideView';
import type { HiddenScreen } from './use-hidden';
import { useViewer } from './use-viewer';

interface HiddenViewerProps {
  screen: HiddenScreen;
}

export function HiddenViewer({ screen }: HiddenViewerProps) {
  return screen.opened !== null && <Viewer screen={screen} start={screen.opened} />;
}

function Viewer({ screen, start }: HiddenViewerProps & { start: number }) {
  const { t } = useTranslation();
  const { viewer, slides, caption, unhide } = useViewer(screen, start);
  return (
    <LazyMotion features={domMax} strict>
      <MotionConfig reducedMotion="user">
        <Frames
          label={t('hidden.title')}
          close={screen.close}
          viewer={viewer}
          slides={slides.map((slide) => ({
            key: slide.memory.id,
            slide: <SlideView {...slide} />,
          }))}
          moves={{ previous: t('viewer.previous'), next: t('viewer.next') }}
          title={t('hidden.caption', caption)}
        >
          <div
            data-floating="player"
            className="absolute inset-x-0 bottom-(--player-pill-bottom) mx-auto w-fit rounded-full p-1 player-glass"
          >
            <Button
              onPress={unhide}
              className="flex min-h-11 items-center gap-2 rounded-full px-4 font-semibold outline-none focus-visible:focus-ring"
            >
              <Eye aria-hidden className="size-6" />
              {t('hidden.unhide')}
            </Button>
          </div>
        </Frames>
      </MotionConfig>
    </LazyMotion>
  );
}
