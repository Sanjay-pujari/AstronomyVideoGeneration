export type MobileTab = 'home' | 'videos' | 'alerts' | 'settings';

export const mobileTabs: Array<{ id: MobileTab; label: string }> = [
  { id: 'home', label: 'Home' },
  { id: 'videos', label: 'Shorts' },
  { id: 'alerts', label: 'Alerts' },
  { id: 'settings', label: 'Settings' }
];

export type NavigationState = {
  activeTab: MobileTab;
  selectedRegionId?: string;
  notificationReady: boolean;
};

export function createInitialNavigationState(selectedRegionId?: string): NavigationState {
  return { activeTab: 'home', selectedRegionId, notificationReady: true };
}
