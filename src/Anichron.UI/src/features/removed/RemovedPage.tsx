import { Check, Film } from 'lucide-react';
import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import { useBlurhash } from '../../shared/use-blurhash';
import { Blurhash } from '../../shared/ui/BlurhashCanvas';
import { Button as TextButton } from '../../shared/ui/Button';
import { Failed } from '../../shared/ui/Failed';
import { LivePhotoIcon } from '../../shared/ui/LivePhotoIcon';
import { ProxyImage } from '../../shared/ui/ProxyImage';
import { Skeleton } from '../../shared/ui/Skeleton';
import { TopBarActions } from '../../shared/ui/TopBarActions';
import { ForgetDialog } from './ForgetDialog';
import type { RemovedRow } from './removed';
import { RemovedViewer } from './RemovedViewer';
import { type RemovedScreen, useRemoved } from './use-removed';
import { useRow } from './use-row';

export function RemovedPage() {
  const screen = useRemoved();
  return (
    <main aria-busy={screen.loading} className="flex flex-col gap-2 p-4">
      {screen.loading && <RowSkeletons count={6} />}
      <Rows screen={screen} />
      {screen.loadingMore && <RowSkeletons count={1} />}
      <MoreFailed screen={screen} />
      {screen.failure && <Failed message={screen.failure} onRetry={screen.retry} />}
      <SelectControls screen={screen} />
      <SelectionBar screen={screen} />
      <Announcement screen={screen} />
      <PageDialog screen={screen} />
      <RemovedViewer screen={screen} />
    </main>
  );
}

function RowSkeletons({ count }: { count: number }) {
  return Array.from({ length: count }, (_, index) => (
    <div key={index} aria-hidden className="flex items-center gap-3 py-1">
      <Skeleton className="rounded-sm size-14 shrink-0" />
      <div className="flex flex-1 flex-col gap-2">
        <Skeleton className="h-4 w-40 rounded-full" />
        <Skeleton className="h-3 w-56 rounded-full" />
      </div>
    </div>
  ));
}

function MoreFailed({ screen }: { screen: RemovedScreen }) {
  const { t } = useTranslation();
  if (!screen.moreFailed) return null;
  return (
    <div className="flex items-center justify-between gap-3 py-2">
      <p className="text-small text-fg-muted">{t('removed.failedMore')}</p>
      <TextButton onPress={screen.retryMore}>{t('failure.retry')}</TextButton>
    </div>
  );
}

function Rows({ screen }: { screen: RemovedScreen }) {
  const { t } = useTranslation();
  if (screen.empty)
    return (
      <div className="flex flex-col gap-1">
        <p className="font-semibold">{t('removed.empty')}</p>
        <p className="text-small text-fg-muted">{t('removed.emptyHint')}</p>
      </div>
    );
  if (screen.rows.length === 0) return null;
  return (
    <ul className="flex flex-col">
      {screen.rows.map((row, index) => (
        <li key={row.memory.id}>
          <RowView screen={screen} row={row} index={index} />
        </li>
      ))}
    </ul>
  );
}

interface RowViewProps {
  screen: RemovedScreen;
  row: RemovedRow;
  index: number;
}

function RowView({ screen, row, index }: RowViewProps) {
  const { t } = useTranslation();
  const nameId = useId();
  return (
    <div
      {...useRow(screen, index, nameId)}
      className="rounded-sm flex w-full cursor-default items-center gap-3 py-1 text-left outline-none select-none [-webkit-touch-callout:none] focus-visible:focus-ring"
    >
      {screen.select.selecting && <Tick screen={screen} index={index} />}
      <Tile row={row} />
      <span className="flex min-w-0 flex-col">
        <span id={nameId} className="truncate font-semibold">
          {row.name}
        </span>
        {row.folder && <span className="truncate text-small text-fg-muted">{row.folder}</span>}
        <span className="text-small text-fg-muted">{t(row.reason.key, row.reason)}</span>
      </span>
    </div>
  );
}

function Tick({ screen, index }: { screen: RemovedScreen; index: number }) {
  const selected = screen.select.isSelected(index);
  return (
    <span
      aria-hidden
      data-selected={selected || undefined}
      onPointerDown={screen.drag.handle(index)}
      className="group flex size-11 shrink-0 touch-none items-center justify-center"
    >
      <span className="flex size-6 items-center justify-center rounded-full border-2 border-fg-muted group-data-selected:border-accent group-data-selected:bg-accent">
        {selected && <Check className="size-4 text-on-accent" />}
      </span>
    </span>
  );
}

function SelectControls({ screen }: { screen: RemovedScreen }) {
  const { t } = useTranslation();
  const { select } = screen;
  if (!select.selecting)
    return (
      screen.rows.length > 0 && (
        <TopBarActions side="trailing">
          <TextButton variant="secondary" onPress={select.start} className="px-3">
            {t('removed.select')}
          </TextButton>
        </TopBarActions>
      )
    );
  return (
    <>
      <TopBarActions side="leading">
        <TextButton variant="secondary" onPress={select.toggleAll} className="px-3">
          {t('removed.selectAll')}
        </TextButton>
      </TopBarActions>
      <TopBarActions side="trailing">
        <TextButton variant="secondary" onPress={select.cancel} className="px-3">
          {t('removed.cancel')}
        </TextButton>
      </TopBarActions>
    </>
  );
}

function SelectionBar({ screen }: { screen: RemovedScreen }) {
  const { t } = useTranslation();
  const { select, forget } = screen;
  if (!select.selecting || select.count === 0) return null;
  return (
    <div
      data-floating="page"
      className="fixed inset-x-4 above-bottom-bar z-10 mx-auto flex max-w-xs justify-center rounded-full bg-surface-raised p-1 shadow-(--shadow-glass)"
    >
      <TextButton variant="danger" onPress={forget.ask(select.selected)}>
        {t('removed.forgetCount', { count: select.count })}
      </TextButton>
    </div>
  );
}

function PageDialog({ screen }: { screen: RemovedScreen }) {
  return screen.opened === null && <ForgetDialog forget={screen.forget} />;
}

function Announcement({ screen: { select } }: { screen: RemovedScreen }) {
  const { t } = useTranslation();
  return (
    <p aria-live="polite" className="sr-only">
      {select.selecting ? t('removed.selected', { count: select.count }) : ''}
    </p>
  );
}

function Tile({ row }: { row: RemovedRow }) {
  const { t } = useTranslation();
  return (
    <span
      role="img"
      aria-label={t(row.kind)}
      className="rounded-sm relative flex size-14 shrink-0 items-center justify-center overflow-hidden bg-surface-raised"
    >
      {row.glyph ? <Film aria-hidden className="size-6 text-fg-muted" /> : <Thumbnail row={row} />}
      {row.live && (
        <LivePhotoIcon className="absolute top-0.5 right-0.5 size-4 rounded-full p-0.5 player-glass" />
      )}
    </span>
  );
}

function Thumbnail({ row: { memory } }: { row: RemovedRow }) {
  const { blurhash, onDecoded } = useBlurhash(memory);
  return (
    <>
      {blurhash && <Blurhash {...blurhash} />}
      <ProxyImage
        src={memory.proxies.thumbnail}
        onDecoded={onDecoded}
        className="relative size-full object-cover"
      />
    </>
  );
}
