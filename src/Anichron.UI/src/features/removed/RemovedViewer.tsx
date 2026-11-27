import { Trash2 } from 'lucide-react';
import { domMax, LazyMotion, MotionConfig } from 'motion/react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { Frames } from '../../shared/ui/Frames';
import { SlideView } from '../../shared/ui/SlideView';
import { ForgetDialog } from './ForgetDialog';
import type { RemovedScreen } from './use-removed';
import { useViewer } from './use-viewer';

interface RemovedViewerProps {
  screen: RemovedScreen;
}

export function RemovedViewer({ screen }: RemovedViewerProps) {
  return screen.opened !== null && <Viewer screen={screen} start={screen.opened} />;
}

function Viewer({ screen, start }: RemovedViewerProps & { start: number }) {
  const { t } = useTranslation();
  const { viewer, slides, caption, current } = useViewer(screen, start);
  return (
    <LazyMotion features={domMax} strict>
      <MotionConfig reducedMotion="user">
        <Frames
          label={t('removed.title')}
          close={screen.close}
          viewer={viewer}
          slides={slides.map((slide) => ({
            key: slide.memory.id,
            slide: <SlideView {...slide} />,
          }))}
          moves={{ previous: t('viewer.previous'), next: t('viewer.next') }}
          title={
            <>
              <span className="block truncate">{caption.path}</span>
              <span className="block font-normal">{t(caption.reason.key)}</span>
              <span className="block font-normal">
                {t(caption.reason.when.key, caption.reason.when)}
              </span>
            </>
          }
        >
          <div className="absolute inset-x-0 bottom-[calc(env(safe-area-inset-bottom)+1rem)] mx-auto w-fit rounded-full p-1 player-glass">
            <Button
              onPress={screen.forget.ask([current])}
              className="flex min-h-11 items-center gap-2 rounded-full px-4 font-semibold outline-none focus-visible:focus-ring"
            >
              <Trash2 aria-hidden className="size-6" />
              {t('removed.forget')}
            </Button>
          </div>
          <ForgetDialog forget={screen.forget} />
        </Frames>
      </MotionConfig>
    </LazyMotion>
  );
}
