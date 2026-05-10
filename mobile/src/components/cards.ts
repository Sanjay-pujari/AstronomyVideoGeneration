export type LoadingState = 'idle' | 'loading' | 'refreshing' | 'error' | 'empty';

export type MobileCardRow = {
  title: string;
  detail?: string;
  status?: string;
  badge?: string;
  href?: string;
};

export type MobileCard = {
  title: string;
  subtitle?: string;
  rows: MobileCardRow[];
  state: LoadingState;
  errorMessage?: string;
  accent?: 'brand' | 'sky' | 'event' | 'video' | 'system' | 'muted';
};

export type MobileScreenModel = {
  title: string;
  subtitle?: string;
  theme: 'dark-astronomy';
  supportsPullToRefresh: boolean;
  cards: MobileCard[];
};

export function createCard(
  title: string,
  subtitle?: string,
  rows: MobileCardRow[] = [],
  state: LoadingState = rows.length === 0 && subtitle === undefined ? 'empty' : 'idle',
  accent: MobileCard['accent'] = 'muted'
): MobileCard {
  return { title, subtitle, rows, state, accent };
}

export function createErrorCard(title: string, message = 'We could not load this section. Pull to refresh or try again later.'): MobileCard {
  return { title, rows: [], state: 'error', errorMessage: message, accent: 'muted' };
}

export function createScreen(title: string, subtitle: string | undefined, cards: MobileCard[]): MobileScreenModel {
  return { title, subtitle, theme: 'dark-astronomy', supportsPullToRefresh: true, cards };
}
