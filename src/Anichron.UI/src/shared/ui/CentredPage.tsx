import type { ReactNode } from 'react';
import { Card } from './Card';
import { tv } from './tv';

const pageStyles = tv({ base: 'grid min-h-dvh place-items-center p-4 glow-backdrop' });

interface CentredPageProps {
  title: string;
  body?: string;
  className?: string;
  children: ReactNode;
}

export function CentredPage({ title, body, className, children }: CentredPageProps) {
  return (
    <main className={pageStyles({ class: className })}>
      <Card className="w-full max-w-sm gap-4 p-6">
        <h1 className="text-title font-semibold">{title}</h1>
        {body && <p className="text-fg-muted">{body}</p>}
        {children}
      </Card>
    </main>
  );
}
