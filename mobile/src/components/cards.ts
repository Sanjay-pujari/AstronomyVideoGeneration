export type MobileCard = {
  title: string;
  subtitle?: string;
  rows?: Array<{ title: string; detail?: string; status?: string }>;
};

export function createCard(title: string, subtitle?: string, rows: MobileCard['rows'] = []): MobileCard {
  return { title, subtitle, rows };
}
