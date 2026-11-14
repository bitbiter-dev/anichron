export function Ring({ className }: { className: string }) {
  return (
    <span
      aria-hidden
      className={`pointer-events-none absolute animate-spin rounded-full border-2 border-accent border-t-transparent motion-reduce:animate-none ${className}`}
    />
  );
}
