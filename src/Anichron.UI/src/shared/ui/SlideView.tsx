import { useTranslation } from 'react-i18next';
import { type Slide, useSlide } from '../use-slide';
import { MemoryImage } from './MemoryImage';
import { LiveButton, Sound } from './PlayerControls';

export function SlideView(props: Slide) {
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

function SlideControls({ replay, live, sound }: SlideState) {
  return (
    <>
      <div className="absolute top-[calc(env(safe-area-inset-top)+3.5rem)] left-2">
        <LiveButton
          shown={replay !== null}
          live={live}
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
