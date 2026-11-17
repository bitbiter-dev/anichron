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

function userAgentHas(platform: RegExp) {
  return platform.test(navigator.userAgent);
}

function onIos() {
  return userAgentHas(/iPhone|iPad/) || (userAgentHas(/Macintosh/) && navigator.maxTouchPoints > 0);
}

export function keepInstallPrompt() {
  const changes = new EventTarget();
  let button: InstallButton | undefined;
  let installedHere = false;
  const offerButton = (offered: InstallButton | undefined) => {
    button = offered;
    changes.dispatchEvent(new Event('change'));
  };
  const answer = async (prompt: InstallPrompt) => {
    await prompt.prompt();
    installedHere = (await prompt.userChoice).outcome === 'accepted';
    offerButton(undefined);
  };
  listen(window, 'beforeinstallprompt', (event) => {
    event.preventDefault();
    let asked = false;
    offerButton({
      install: () => {
        if (asked) return;
        asked = true;
        void answer(event as InstallPrompt);
      },
    });
  });
  listen(window, 'appinstalled', () => {
    installedHere = true;
    offerButton(undefined);
  });
  return {
    offer: (): InstallOffer => {
      if (installedHere || installed()) return 'none';
      if (onIos()) return 'ios';
      return button ?? (userAgentHas(/Android/) ? 'android' : 'none');
    },
    subscribe: (listener: () => void) => listen(changes, 'change', listener),
  };
}
