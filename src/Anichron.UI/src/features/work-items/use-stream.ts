import { useQueryClient } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { listen } from '../../shared/listen';
import { merge } from './cache';

const onVisibilityChange = (changed: () => void) => listen(document, 'visibilitychange', changed);
const isVisible = () => document.visibilityState === 'visible';

export function useStream(cursor: string | undefined) {
  const { api } = useRouter().options.context;
  const queryClient = useQueryClient();
  const visible = useSyncExternalStore(onVisibilityChange, isVisible);
  const lastEventId = useRef<string>(undefined);
  const [reconnecting, setReconnecting] = useState(false);
  const [gone, setGone] = useState(false);
  useEffect(() => {
    if (cursor === undefined || !visible) return;
    const controller = new AbortController();
    void api.workItems.follow(lastEventId.current ?? cursor, controller.signal, {
      event(id, item) {
        lastEventId.current = id;
        merge(queryClient, item);
      },
      reconnecting: setReconnecting,
      gone: () => {
        setGone(true);
      },
    });
    return () => {
      controller.abort();
    };
  }, [api, queryClient, cursor, visible]);
  return { reconnecting, gone };
}
