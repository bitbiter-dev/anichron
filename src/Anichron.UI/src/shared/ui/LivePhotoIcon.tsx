interface LivePhotoIconProps {
  className?: string;
}

export function LivePhotoIcon({ className }: LivePhotoIconProps) {
  return (
    <svg
      aria-hidden
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      className={className}
    >
      <circle cx={12} cy={12} r={2.5} fill="currentColor" stroke="none" />
      <circle cx={12} cy={12} r={6} />
      <circle
        cx={12}
        cy={12}
        r={10}
        strokeDasharray="0 3.1416"
        strokeLinecap="round"
        strokeWidth={1.75}
      />
    </svg>
  );
}
