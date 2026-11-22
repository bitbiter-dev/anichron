export interface Display {
  decodesJpegXl(): Promise<boolean>;
  longEdge(): number;
}

function smallestJpegXl() {
  return 'data:image/jxl;base64,/woAkAEAFIgGAKwAocHUAQAAVKiMMm7wcq7nyw8dFtM2rjO01baFJYyJZxwsJBwlkDZDYpBUAg==';
}

export function browserDisplay(): Display {
  return {
    decodesJpegXl() {
      const probe = new Image();
      probe.src = smallestJpegXl();
      return probe.decode().then(
        () => true,
        () => false,
      );
    },
    longEdge: () => Math.max(screen.width, screen.height),
  };
}

export async function showsCompactPreviews(display: Display) {
  return display.longEdge() < 1024 && (await display.decodesJpegXl());
}
