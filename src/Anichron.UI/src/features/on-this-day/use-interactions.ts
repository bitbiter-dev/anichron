import { useMutation } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import type { Change, Flag } from '../../api/assets';
import type { Memory } from '../../api/flashbacks';
import { createToasts } from '../../shared/toasts';
import { burstIdOf, otherFrames } from './burst';
import { burstQuery, interactionMutation } from './day';
import type { Place } from './story';

export type Toggle = Exclude<Flag, 'hidden'>;

interface StoryControls {
  memory: Memory;
  hide: (place?: Place) => Place;
  undo: (place: Place) => void;
  closeDetails: () => void;
}

export function useInteractions(date: string, story: StoryControls) {
  const { api, queryClient } = useRouter().options.context;
  const [toasts] = useState(createToasts);
  const { mutateAsync } = useMutation(interactionMutation(api, queryClient, date));

  function notSaved() {
    toasts.show({ message: 'story.notSaved' });
  }

  function send(memory: Memory, change: Change, rollBack: () => void = () => undefined) {
    mutateAsync({ id: memory.id, change }).catch(() => {
      rollBack();
      notSaved();
    });
  }

  function hideWith(memory: Memory, onUndo: () => void, rollBack: () => void) {
    const key = toasts.show({
      message: 'story.hidden',
      action: { label: 'story.undo', onAction: onUndo },
    });
    send(memory, { hidden: true }, () => {
      toasts.close(key);
      rollBack();
    });
  }

  function hideMemory(memory: Memory) {
    const place = story.hide();
    hideWith(
      memory,
      () => {
        story.undo(place);
        send(memory, { hidden: false }, () => story.hide(place));
      },
      () => {
        story.undo(place);
      },
    );
  }

  function hideFrame(frame: Memory) {
    hideWith(
      frame,
      () => {
        send(frame, { hidden: false });
      },
      () => undefined,
    );
  }

  function hide(memory: Memory) {
    const card = story.memory;
    if (otherFrames(card) < 1) {
      story.closeDetails();
      hideMemory(memory);
      return;
    }
    void queryClient
      .query({ ...burstQuery(api, burstIdOf(card)), staleTime: 'static' })
      .then(() => {
        hideFrame(memory);
      }, notSaved);
  }

  return {
    toasts,
    toggle: (flag: Toggle) => (on: boolean) => {
      send(story.memory, { [flag]: on });
    },
    toggleFrame: (frame: Memory) => (flag: Toggle) => (on: boolean) => {
      send(frame, { [flag]: on });
    },
    hide: () => {
      hide(story.memory);
    },
    hideFrame: hide,
  };
}
