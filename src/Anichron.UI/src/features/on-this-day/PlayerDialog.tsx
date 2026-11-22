import type { ReactNode } from 'react';
import { Dialog, Modal, ModalOverlay } from 'react-aria-components';
import { tv } from '../../shared/ui/tv';

const overlayStyles = tv({
  base: 'fixed inset-0',
  variants: { layer: { story: 'z-20', frames: 'z-[25] bg-player' } },
});

interface PlayerDialogProps {
  label: string;
  layer: 'story' | 'frames';
  close: () => void;
  children: ReactNode;
}

export function PlayerDialog({ label, layer, close, children }: PlayerDialogProps) {
  return (
    <ModalOverlay isOpen onOpenChange={close} className={overlayStyles({ layer })}>
      <Modal className="size-full">
        <Dialog aria-label={label} className="relative size-full text-on-player outline-none">
          {children}
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
