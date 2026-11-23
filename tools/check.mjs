import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const html = await readFile(path.join(root, 'index.html'), 'utf8');
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
assert.equal(ids.length, new Set(ids).size, 'HTML IDs must be unique');
for (const [, id] of html.matchAll(/href="#([^"]+)"/g)) assert(ids.includes(id), `Missing anchor: ${id}`);
for (const [, file] of html.matchAll(/(?:href|src)="\.\/([^"]+)"/g)) await access(path.join(root, file));
const downloads = [...html.matchAll(/<a\b[^>]*class="download-button"[^>]*>/g)];
assert.equal(downloads.length, 2, 'Both download calls to action should be present');
for (const [tag] of downloads) {
  const href = tag.match(/href="([^"]+)"/)?.[1];
  const url = new URL(href);
  assert.equal(url.origin, 'https://github.com', 'Installer must come from GitHub');
  assert.equal(url.pathname, '/matviu08/helperbar/releases/download/v0.13.1/HelperBar-Setup-x64.exe', 'Download must match the displayed release');
  assert(!/\bdisabled\b|aria-disabled="true"/.test(tag), 'Installer links must be enabled');
  const description = tag.match(/aria-describedby="([^"]+)"/)?.[1];
  assert(ids.includes(description), 'Each download needs an accessible requirements description');
}
assert(!/Скоро|Інсталер готується|інсталер ще готується|Дата випуску інсталятора ще/.test(html), 'Outdated installer placeholders must be removed');
assert.equal([...html.matchAll(/data-mode-button=/g)].length, 4);
assert.equal([...html.matchAll(/data-pane=/g)].length, 4);
assert(html.includes('lang="uk"'));
assert(!/https?:\/\/[^"\s]+\.(js|css)["\s]/i.test(html), 'Static assets should load without external services');
console.log('PASS: anchors, unique IDs, local assets, four demo modes, Ukrainian language, enabled release download links, no external runtime dependencies.');
