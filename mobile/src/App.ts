import { createInitialNavigationState, type MobileTab } from './navigation/AppNavigator.js';
import { createAlertPreferencesScreen } from './screens/AlertPreferencesScreen.js';
import { createAlertsScreen } from './screens/AlertsScreen.js';
import { createEventsScreen } from './screens/EventsScreen.js';
import { createHomeScreen } from './screens/HomeScreen.js';
import { createRegionsScreen } from './screens/RegionsScreen.js';
import { createSettingsScreen } from './screens/SettingsScreen.js';
import { createSkyScreen } from './screens/SkyScreen.js';
import { createSystemAdminScreen } from './screens/SystemAdminScreen.js';
import { createVideosScreen } from './screens/VideosScreen.js';
import type { MobileHomeData } from './services/api.js';
import { mockMobileHomeData } from './services/mockData.js';

function createScreenForTab(data: MobileHomeData, activeTab: MobileTab, selectedRegionId?: string) {
  switch (activeTab) {
    case 'home':
      return createHomeScreen(data, selectedRegionId);
    case 'sky':
      return createSkyScreen(data, selectedRegionId);
    case 'events':
      return createEventsScreen(data);
    case 'alerts':
      return createAlertsScreen(data);
    case 'videos':
      return createVideosScreen(data);
    case 'settings':
      return createSettingsScreen(data, selectedRegionId);
  }
}

export function createMobileAppModel(data: MobileHomeData = mockMobileHomeData, activeTab: MobileTab = 'home', selectedRegionId = data.regions[0]?.id) {
  const navigation = { ...createInitialNavigationState(selectedRegionId), activeTab };
  const screen = createScreenForTab(data, activeTab, selectedRegionId);

  return {
    brand: 'AstroPulse',
    theme: {
      mode: 'dark',
      background: '#050816',
      card: '#111827',
      accent: '#8B5CF6'
    },
    navigation,
    screen,
    secondaryScreens: {
      regions: createRegionsScreen(data.regions),
      alertPreferences: createAlertPreferencesScreen(data, selectedRegionId),
      systemAdminLite: createSystemAdminScreen(data)
    }
  };
}
