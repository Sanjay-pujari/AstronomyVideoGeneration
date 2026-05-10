export type MobileTab = 'home' | 'sky' | 'events' | 'videos' | 'settings';

export const mobileTabs: Array<{ id: MobileTab; label: string; icon: string }> = [
  { id: 'home', label: 'Home', icon: 'constellation' },
  { id: 'sky', label: 'Sky', icon: 'telescope' },
  { id: 'events', label: 'Events', icon: 'meteor' },
  { id: 'videos', label: 'Videos', icon: 'play' },
  { id: 'settings', label: 'Settings', icon: 'gear' }
];

export type NavigationState = {
  activeTab: MobileTab;
  selectedRegionId?: string;
  notificationReady: boolean;
  bottomTabs: typeof mobileTabs;
  secondaryRoutes: Array<'regions' | 'systemAdminLite'>;
};

export function createInitialNavigationState(selectedRegionId?: string): NavigationState {
  return {
    activeTab: 'home',
    selectedRegionId,
    notificationReady: true,
    bottomTabs: mobileTabs,
    secondaryRoutes: ['regions', 'systemAdminLite']
  };
}
