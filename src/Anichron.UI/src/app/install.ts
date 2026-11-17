import { listen } from '../shared/listen';

interface InstallPrompt extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
}

interface InstallButton {
  install: () => void;
}

export type InstallOffer = 'none' | 'ios' | 'android' | InstallButton;
export type Install = ReturnType<typeof keepInstallPrompt>;

function installed() {
  return (
    window.matchMedia('(display-mode: standalone)').matches ||
    (navigator as { standalone?: boolean }).standalone === true
  );
}

function on(platform: RegExp) {
  return platform.test(navigator.userAgent);
}

function onIos() {
  return on(/iPhone/) || (on(/Macintosh/) && navigator.maxTouchPoints > 0);
}

export function keepInstallPrompt() {
  const changes = new EventTarget();
  let button: InstallButton | undefined;
  let accepted = false;
  const offerButton = (offered: InstallButton | undefined) => {
    button = offered;
    changes.dispatchEvent(new Event('change'));
  };
  const answer = async (prompt: InstallPrompt) => {
    await prompt.prompt();
    accepted = (await prompt.userChoice).outcome === 'accepted';
    offerButton(undefined);
  };
  listen(window, 'beforeinstallprompt', (event) => {
    event.preventDefault();
    offerButton({ install: () => void answer(event as InstallPrompt) });
  });
  return {
    offer: (): InstallOffer => {
      if (accepted || installed()) return 'none';
      if (onIos()) return 'ios';
      return button ?? (on(/Android/) ? 'android' : 'none');
    },
    subscribe: (listener: () => void) => listen(changes, 'change', listener),
  };
}
