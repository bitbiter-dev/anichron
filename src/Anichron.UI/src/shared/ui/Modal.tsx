import type { ReactNode } from 'react';
import { Dialog, Heading, Modal as AriaModal, ModalOverlay } from 'react-aria-components';

interface ModalProps {
  title: string;
  onClose: () => void;
  children: ReactNode;
}

export function Modal({ title, onClose, children }: ModalProps) {
  return (
    <ModalOverlay
      isOpen
      isDismissable
      onOpenChange={onClose}
      className="fixed inset-0 z-40 flex items-center justify-center bg-player/50 p-4"
    >
      <AriaModal className="shadow-glass w-full max-w-sm rounded-card bg-surface-raised p-6 text-fg">
        <Dialog className="flex flex-col gap-4 outline-none">
          <Heading slot="title" className="text-title font-semibold">
            {title}
          </Heading>
          {children}
        </Dialog>
      </AriaModal>
    </ModalOverlay>
  );
}
