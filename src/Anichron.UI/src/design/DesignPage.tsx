import type { ReactNode } from 'react';
import { contrastRatio, themeColours, type Theme, type ThemeColours } from './contrast';

const themes: Theme[] = ['light', 'dark'];

const typeScale = [
  ['text-title', 'text-title font-semibold'],
  ['text-body', 'text-body'],
  ['text-small', 'text-small'],
];

const ratio = (a = '', b = '') => contrastRatio(a, b).toFixed(2);

export function DesignPage({ tokens }: { tokens: string }) {
  const colours = themeColours(tokens);
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
      <Section title="Glass">
        <div className="grid gap-6 md:grid-cols-2">
          {themes.map((theme) => (
            <LoginMock key={theme} theme={theme} />
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

function LoginMock({ theme }: { theme: Theme }) {
  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-small font-semibold">{theme}</h3>
      <div
        className="grid h-[560px] place-items-center p-4 text-fg glow-backdrop"
        style={{ colorScheme: theme }}
      >
        <div className="flex w-full max-w-sm flex-col gap-4 rounded-card p-6 glass-card">
          <h4 className="mb-2 text-title font-semibold">Sign in to Anichron</h4>
          <MockField label="Username or email" value="mira" />
          <MockField label="Password" value="••••••••••" />
          <span className="mt-2 flex min-h-11 items-center justify-center rounded-full bg-accent px-6 font-semibold text-on-accent lift-accent">
            Sign in
          </span>
        </div>
      </div>
    </div>
  );
}

function MockField({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-small font-medium">{label}</span>
      <span className="flex min-h-11 items-center rounded-field border border-border bg-surface px-3">
        {value}
      </span>
    </div>
  );
}
