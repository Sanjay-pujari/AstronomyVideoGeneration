import { mkdir, copyFile } from 'node:fs/promises';
await mkdir('dist', { recursive: true });
await mkdir('dist/assets/styles', { recursive: true });
await copyFile('index.html', 'dist/index.html');
await copyFile('src/styles/app.css', 'dist/assets/styles/app.css');
await copyFile('frontend-api-health.json', 'dist/frontend-api-health.json');
