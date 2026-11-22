import { Eye } from 'lucide-react';
import { domMax, LazyMotion, MotionConfig } from 'motion/react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { Frames } from '../../shared/ui/Frames';
import { MemoryImage } from '../../shared/ui/MemoryImage';
import { LiveButton, Sound } from '../../shared/ui/PlayerControls';
import type { HiddenScreen } from './use-hidden';
import { type Slide, useSlide } from './use-slide';
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
          moves={{ previous: t('hidden.previous'), next: t('hidden.next') }}
          title={t('hidden.caption', caption)}
        >
          <div className="absolute inset-x-0 bottom-[calc(env(safe-area-inset-bottom)+1rem)] mx-auto w-fit rounded-full p-1 player-glass">
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

function SlideView(props: Slide) {
  const { t } = useTranslation();
  const slide = useSlide(props);
  return (
    <div className="relative size-full">
      <MemoryImage
        label={t(slide.name)}
        blurhash={slide.blurhash}
        {...slide.picture}
        className="absolute inset-0"
      />
      {slide.video && <SlideVideo key={slide.reload} {...slide.video} />}
      <SlideFailed failed={slide.failed} />
      <SlideControls {...slide} />
    </div>
  );
}

function SlideFailed({ failed }: { failed: boolean }) {
  const { t } = useTranslation();
  if (!failed) return null;
  return (
    <p className="absolute inset-x-4 top-1/2 -translate-y-1/2 text-center font-semibold">
      {t('story.failed')}
    </p>
  );
}

type SlideState = ReturnType<typeof useSlide>;

function SlideControls({ replay, video, sound }: SlideState) {
  return (
    <>
      <div className="absolute top-[calc(env(safe-area-inset-top)+3.5rem)] left-2">
        <LiveButton
          shown={replay !== null}
          live={video ? 'playing' : 'still'}
          play={() => {
            replay?.();
          }}
        />
      </div>
      {sound && <Sound shown hint={false} label={sound.label} toggle={sound.toggle} />}
    </>
  );
}

function SlideVideo({ muted, ...video }: NonNullable<SlideState['video']>) {
  return (
    <video
      {...video}
      ref={(element) => {
        if (element) element.muted = muted;
      }}
      muted
      autoPlay
      playsInline
      aria-hidden
      className="absolute inset-0 size-full object-contain"
    />
  );
}
