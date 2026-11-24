import { Film } from 'lucide-react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { useBlurhash } from '../../shared/use-blurhash';
import { Blurhash } from '../../shared/ui/BlurhashCanvas';
import { Button as TextButton } from '../../shared/ui/Button';
import { Failed } from '../../shared/ui/Failed';
import { LivePhotoIcon } from '../../shared/ui/LivePhotoIcon';
import { ProxyImage } from '../../shared/ui/ProxyImage';
import { Skeleton } from '../../shared/ui/Skeleton';
import type { RemovedRow } from './removed';
import { RemovedViewer } from './RemovedViewer';
import { type RemovedScreen, useRemoved } from './use-removed';

export function RemovedPage() {
  const screen = useRemoved();
  return (
    <main aria-busy={screen.loading} className="flex flex-col gap-2 p-4">
      {screen.loading && <RowSkeletons count={6} />}
      <Rows screen={screen} />
      {screen.loadingMore && <RowSkeletons count={1} />}
      <MoreFailed screen={screen} />
      {screen.failure && <Failed message={screen.failure} onRetry={screen.retry} />}
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
          <RowView row={row} onPress={screen.open(index)} />
        </li>
      ))}
    </ul>
  );
}

interface RowViewProps {
  row: RemovedRow;
  onPress: () => void;
}

function RowView({ row, onPress }: RowViewProps) {
  const { t } = useTranslation();
  return (
    <Button
      onPress={onPress}
      className="rounded-sm flex w-full items-center gap-3 py-1 text-left outline-none focus-visible:focus-ring"
    >
      <Tile row={row} />
      <span className="flex min-w-0 flex-col">
        <span className="truncate font-semibold">{row.name}</span>
        {row.folder && <span className="truncate text-small text-fg-muted">{row.folder}</span>}
        <span className="text-small text-fg-muted">{t(row.reason.key, row.reason)}</span>
      </span>
    </Button>
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
