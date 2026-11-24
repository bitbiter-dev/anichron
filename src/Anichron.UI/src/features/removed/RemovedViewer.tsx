import { domMax, LazyMotion, MotionConfig } from 'motion/react';
import { useTranslation } from 'react-i18next';
import { Frames } from '../../shared/ui/Frames';
import { SlideView } from '../../shared/ui/SlideView';
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
  const { viewer, slides, caption } = useViewer(screen, start);
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
              <span className="block font-normal">{t(caption.reason.key, caption.reason)}</span>
            </>
          }
        >
          {null}
        </Frames>
      </MotionConfig>
    </LazyMotion>
  );
}
