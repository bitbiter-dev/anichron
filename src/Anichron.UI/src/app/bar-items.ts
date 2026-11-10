import type { ParseKeys } from 'i18next';
import { CalendarHeart, Settings, type LucideIcon } from 'lucide-react';

export interface BarItem {
  to: '/' | '/settings';
  label: ParseKeys;
  icon: LucideIcon;
}

export const barItems: readonly BarItem[] = [
  { to: '/', label: 'shell.today', icon: CalendarHeart },
  { to: '/settings', label: 'shell.settings', icon: Settings },
];
