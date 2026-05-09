import { createInitialNavigationState, type MobileTab } from './navigation/AppNavigator.js';
import { createAlertsScreen } from './screens/AlertsScreen.js';
import { createHomeScreen } from './screens/HomeScreen.js';
import { createSettingsScreen } from './screens/SettingsScreen.js';
import { createVideosScreen } from './screens/VideosScreen.js';
import type { MobileHomeData } from './services/api.js';
import { mockMobileHomeData } from './services/mockData.js';

export function createMobileAppModel(data: MobileHomeData = mockMobileHomeData, activeTab: MobileTab = 'home', selectedRegionId = data.regions[0]?.id) {
  const navigation = { ...createInitialNavigationState(selectedRegionId), activeTab };
  const screen = activeTab === 'home'
    ? createHomeScreen(data, selectedRegionId)
    : activeTab === 'videos'
      ? createVideosScreen(data.latestShorts)
      : activeTab === 'alerts'
        ? createAlertsScreen([...data.upcomingEvents, ...data.topEvents])
        : createSettingsScreen(data, selectedRegionId);

  return { brand: 'AstroPulse', navigation, screen };
}
