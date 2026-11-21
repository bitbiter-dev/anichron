import type { ReactNode } from 'react';
import { Dialog, Heading, Modal as AriaModal, ModalOverlay } from 'react-aria-components';

interface ModalProps {
  title: string;
  onClose: () => void;
  role?: 'dialog' | 'alertdialog';
  children: ReactNode;
}

export function Modal({ title, onClose, role, children }: ModalProps) {
  return (
    <ModalOverlay
      isOpen
      isDismissable
      onOpenChange={onClose}
      className="fixed inset-0 z-40 flex items-center justify-center bg-scrim/50 p-4"
    >
      <AriaModal className="w-full max-w-sm rounded-card bg-surface-raised p-6 text-fg shadow-(--shadow-glass)">
        <Dialog role={role} className="flex flex-col gap-4 outline-none">
          <Heading slot="title" className="text-title font-semibold">
            {title}
          </Heading>
          {children}
        </Dialog>
      </AriaModal>
    </ModalOverlay>
  );
}
