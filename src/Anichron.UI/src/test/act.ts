import { act } from 'react';

export function inAct(run: () => void) {
  if ((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT) act(run);
  else run();
}
