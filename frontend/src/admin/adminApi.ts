import { api, loadDashboardData } from '../services/api.js';

export const adminApi = {
  loadDashboardData,
  getOpsDashboard: api.getOpsDashboard,
  getOpsRuns: api.getOpsRuns,
  getOpsRun: api.getOpsRun,
  getAnalyticsDashboard: api.getAnalyticsDashboard,
  getAnalyticsInsights: api.getAnalyticsInsights,
  getAiOptimizationRecommendations: api.getAiOptimizationRecommendations,
  getOptimizationPlan: api.getOptimizationPlan,
  getTokenHealth: api.getTokenHealth
};

export { api as adminActions };
