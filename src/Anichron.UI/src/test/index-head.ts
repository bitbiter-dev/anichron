import indexHtml from '../../index.html?raw';
import { themeColour } from '../shared/theme';

export function addThemeColourTags() {
  const index = new DOMParser().parseFromString(indexHtml, 'text/html');
  document.head.append(...index.querySelectorAll(themeColour));
}

export function removeThemeColourTags() {
  for (const tag of document.querySelectorAll(themeColour)) tag.remove();
}

export function themeColourTags() {
  return [...document.querySelectorAll<HTMLMetaElement>(themeColour)].map(
    (tag) => `${tag.getAttribute('media') ?? 'any scheme'}: ${tag.content}`,
  );
}
