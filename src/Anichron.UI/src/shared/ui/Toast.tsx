import { useTranslation } from 'react-i18next';
import {
  Button,
  Text,
  UNSTABLE_Toast as Toast,
  UNSTABLE_ToastContent as ToastContent,
  UNSTABLE_ToastRegion as ToastRegion,
} from 'react-aria-components';
import type { ToastAction, Toasts as ToastQueue } from '../toasts';
import { focusVariant, tv } from './tv';

export const toastStyles = tv({
  base: 'flex min-h-11 items-center gap-4 rounded-field bg-surface-raised py-1 pr-1 pl-4 text-small text-fg shadow-(--shadow-glass)',
  variants: {
    ...focusVariant,
  },
});

export const toastActionStyles =
  'min-h-11 rounded-full px-4 font-semibold text-accent outline-none focus-visible:focus-ring';

interface ToastsProps {
  toasts: ToastQueue;
}

export function Toasts({ toasts }: ToastsProps) {
  const { t } = useTranslation();
  return (
    <ToastRegion
      queue={toasts.queue}
      className="fixed inset-x-0 toast-region z-30 flex flex-col items-center gap-2 px-4 outline-none"
    >
      {({ toast }) => (
        <Toast toast={toast} className={toastStyles}>
          <ToastContent role="status" className="py-2">
            <Text slot="title">{t(toast.content.message, { ...toast.content.values })}</Text>
          </ToastContent>
          <Action
            action={toast.content.action}
            close={() => {
              toasts.close(toast.key);
            }}
          />
        </Toast>
      )}
    </ToastRegion>
  );
}

interface ActionProps {
  action: ToastAction | undefined;
  close: () => void;
}

function Action({ action, close }: ActionProps) {
  const { t } = useTranslation();
  if (!action) return null;
  return (
    <Button
      onPress={() => {
        close();
        action.onAction();
      }}
      className={toastActionStyles}
    >
      {t(action.label)}
    </Button>
  );
}
