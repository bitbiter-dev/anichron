import type { ReactNode } from 'react';
import { contrastRatio, themeColours, type Theme, type ThemeColours } from './contrast';

const themes: Theme[] = ['light', 'dark'];

interface Accent {
  name: string;
  colours: Record<Theme, { accent: string; onAccent: string }>;
}

const otherAccents: Accent[] = [
  {
    name: 'Coral',
    colours: {
      light: { accent: '#c4402f', onAccent: '#ffffff' },
      dark: { accent: '#ff8a75', onAccent: '#1f1c19' },
    },
  },
  {
    name: 'Teal',
    colours: {
      light: { accent: '#0f766e', onAccent: '#ffffff' },
      dark: { accent: '#2cc5b0', onAccent: '#1f1c19' },
    },
  },
];

const typeScale = [
  ['text-title', 'text-title font-semibold'],
  ['text-body', 'text-body'],
  ['text-small', 'text-small'],
];

const ratio = (a = '', b = '') => contrastRatio(a, b).toFixed(2);

const tokenAccent = (colours: ThemeColours) =>
  Object.fromEntries(
    themes.map((theme) => [
      theme,
      { accent: colours[theme].accent ?? '', onAccent: colours[theme]['on-accent'] ?? '' },
    ]),
  ) as Accent['colours'];

export function DesignPage({ tokens }: { tokens: string }) {
  const colours = themeColours(tokens);
  const accents: Accent[] = [
    { name: 'Warm amber (in the tokens)', colours: tokenAccent(colours) },
    ...otherAccents,
  ];
  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-10 p-6">
      <h1 className="text-title font-semibold">Anichron design</h1>
      <Section title="Palette">
        <div className="grid gap-6 md:grid-cols-2">
          {themes.map((theme) => (
            <Palette key={theme} theme={theme} colours={colours} />
          ))}
        </div>
      </Section>
      <Section title="Type scale">
        {typeScale.map(([token, className]) => (
          <p key={token} className={className}>
            {token}: Sign in to Anichron
          </p>
        ))}
      </Section>
      <Section title="Accent candidates">
        <div className="grid gap-6 md:grid-cols-3">
          {accents.map((accent) => (
            <AccentCandidate key={accent.name} accent={accent} colours={colours} />
          ))}
        </div>
      </Section>
    </main>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-body font-semibold">{title}</h2>
      {children}
    </section>
  );
}

function Palette({ theme, colours }: { theme: Theme; colours: ThemeColours }) {
  const values = colours[theme];
  return (
    <div
      className="flex flex-col gap-2 rounded-card p-4"
      style={{ backgroundColor: values.surface, color: values.fg }}
    >
      <h3 className="font-semibold">{theme}</h3>
      {Object.entries(values).map(([name, value]) => (
        <div key={name} className="flex items-center gap-3 text-small">
          <span
            className="size-8 shrink-0 rounded-field border"
            style={{ backgroundColor: value, borderColor: values.border }}
          />
          <span className="w-32 font-medium">{name}</span>
          <span className="font-mono">{value}</span>
          <span style={{ color: values['fg-muted'] }}>
            {ratio(value, values.surface)} on surface · {ratio(value, values['surface-raised'])} on
            raised
          </span>
        </div>
      ))}
    </div>
  );
}

function AccentCandidate({ accent, colours }: { accent: Accent; colours: ThemeColours }) {
  return (
    <div className="flex flex-col gap-3">
      <h3 className="font-semibold">{accent.name}</h3>
      {themes.map((theme) => {
        const { accent: fill, onAccent } = accent.colours[theme];
        const values = colours[theme];
        return (
          <div
            key={theme}
            className="flex flex-col gap-3 rounded-card p-4 text-small"
            style={{ backgroundColor: values['surface-raised'], color: values.fg }}
          >
            <span className="font-medium">{theme}</span>
            <span
              className="flex min-h-11 items-center justify-center rounded-full px-6 text-body font-semibold"
              style={{
                backgroundColor: fill,
                color: onAccent,
                outline: `2px solid ${fill}`,
                outlineOffset: 2,
              }}
            >
              Sign in
            </span>
            <span>
              {ratio(onAccent, fill)} text on accent · focus {ratio(fill, values.surface)} on
              surface, {ratio(fill, values['surface-raised'])} on raised
            </span>
          </div>
        );
      })}
    </div>
  );
}
