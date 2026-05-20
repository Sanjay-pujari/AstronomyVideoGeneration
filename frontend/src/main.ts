import { startAdminApp } from './admin/adminApp.js';
import { isAdminRoute } from './admin/adminRoutes.js';
import { startPublicApp } from './public/publicApp.js';

const root = document.getElementById('root');
if (!root) throw new Error('Root element #root not found');

if (isAdminRoute(location.pathname)) startAdminApp(root);
else startPublicApp(root);
