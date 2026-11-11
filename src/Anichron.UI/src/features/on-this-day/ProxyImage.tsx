import { decoded, useRecovering } from './media';

interface ProxyImageProps {
  src: string | null;
  className?: string;
  onDecoded?: () => void;
}

export function ProxyImage({ src, className, onDecoded = () => undefined }: ProxyImageProps) {
  const { reloads, onError } = useRecovering();
  if (!src) return null;
  return (
    <img
      key={reloads}
      src={src}
      alt=""
      className={className}
      onError={onError}
      onLoad={(event) => {
        decoded(event.currentTarget, onDecoded);
      }}
    />
  );
}
