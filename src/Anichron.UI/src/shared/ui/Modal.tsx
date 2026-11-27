import type { ReactNode } from 'react';
import { Dialog, Heading, Modal as AriaModal, ModalOverlay } from 'react-aria-components';

interface ModalProps {
  title: string;
  onClose: () => void;
  alert?: boolean;
  children: ReactNode;
}

export function Modal({ title, onClose, alert = false, children }: ModalProps) {
  return (
    <ModalOverlay
      isOpen
      isDismissable={!alert}
      onOpenChange={onClose}
      className="fixed inset-0 z-40 flex items-center justify-center bg-scrim/50 p-4"
    >
      <AriaModal className="w-full max-w-sm rounded-card bg-surface-raised p-6 text-fg shadow-(--shadow-glass)">
        <Dialog
          role={alert ? 'alertdialog' : 'dialog'}
          className="flex flex-col gap-4 outline-none"
        >
          <Heading slot="title" className="text-title font-semibold">
            {title}
          </Heading>
          {children}
        </Dialog>
      </AriaModal>
    </ModalOverlay>
  );
}
