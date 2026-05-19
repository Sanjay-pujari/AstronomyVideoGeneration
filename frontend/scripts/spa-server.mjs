import { createServer } from 'node:http';
import { access, readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const defaultRoot = path.resolve(__dirname, '../dist');

const MIME_TYPES = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.svg', 'image/svg+xml'],
  ['.png', 'image/png'],
  ['.jpg', 'image/jpeg'],
  ['.jpeg', 'image/jpeg'],
  ['.ico', 'image/x-icon']
]);

function toFsPath(rootDir, requestPath) {
  const normalizedPath = path.normalize(requestPath).replace(/^([.]{2}[\\/])+/, '');
  return path.join(rootDir, normalizedPath);
}

async function fileExists(filePath) {
  try {
    const fileStat = await stat(filePath);
    return fileStat.isFile();
  } catch {
    return false;
  }
}

async function readAsset(rootDir, requestPath) {
  const cleanPath = requestPath.replace(/^\/+/, '');
  const filePath = toFsPath(rootDir, cleanPath || 'index.html');
  if (!(await fileExists(filePath))) return undefined;
  const ext = path.extname(filePath).toLowerCase();
  return {
    body: await readFile(filePath),
    contentType: MIME_TYPES.get(ext) || 'application/octet-stream'
  };
}

export async function resolveRequest(rootDir, requestPath) {
  const requested = await readAsset(rootDir, requestPath);
  if (requested) return { status: 200, ...requested };

  const indexHtml = await readAsset(rootDir, 'index.html');
  if (!indexHtml) return { status: 500, body: Buffer.from('Missing dist/index.html'), contentType: 'text/plain; charset=utf-8' };
  return { status: 200, ...indexHtml };
}

export function createSpaServer({ rootDir = defaultRoot } = {}) {
  return createServer(async (req, res) => {
    try {
      const requestUrl = new URL(req.url || '/', 'http://localhost');
      const { status, body, contentType } = await resolveRequest(rootDir, decodeURIComponent(requestUrl.pathname));
      res.writeHead(status, { 'content-type': contentType, 'cache-control': 'no-store' });
      res.end(body);
    } catch (error) {
      res.writeHead(500, { 'content-type': 'text/plain; charset=utf-8' });
      res.end(error instanceof Error ? error.message : 'Unknown server error');
    }
  });
}

export async function ensureBuildExists(rootDir = defaultRoot) {
  await access(path.join(rootDir, 'index.html'));
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const port = Number(process.env.PORT || 3000);
  await ensureBuildExists(defaultRoot);
  const server = createSpaServer({ rootDir: defaultRoot });
  server.listen(port, () => {
    console.log(`SPA preview server listening at http://localhost:${port}`);
  });
}
