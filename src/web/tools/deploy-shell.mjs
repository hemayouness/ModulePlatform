#!/usr/bin/env node
/**
 * Copies the built Shell into the ASP.NET Core application's wwwroot.
 *
 * The Shell is NOT a separate IIS site. It is static content served by the same
 * ASP.NET Core application that serves the API and the module artifacts, so the
 * whole system is one origin: no CORS, no cross-site cookie problems, and a
 * single deployment unit.
 *
 * The Shell's own federation artifacts (its remoteEntry.json and its shared
 * dependency bundles) land at the web root, which is exactly where
 * `initFederation()` looks for them by default (`deployUrl` defaults to './').
 */

import { rm, mkdir, cp, readdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const workspaceRoot = resolve(fileURLToPath(new URL('.', import.meta.url)), '..');
const source = join(workspaceRoot, 'dist', 'shell', 'browser');
const target = resolve(workspaceRoot, '..', 'module-platform', 'ModulePlatform.Api', 'wwwroot');

if (!existsSync(source)) {
  console.error(`✗ no Shell build at dist/shell/browser — run: npm run build:shell`);
  process.exit(1);
}

// Replace wholesale: the Shell's filenames are content-hashed, so leaving stale
// files behind would slowly accumulate dead bundles.
await rm(target, { recursive: true, force: true });
await mkdir(target, { recursive: true });
await cp(source, target, { recursive: true });

const files = await readdir(target);
console.log(`✓ Shell deployed to ModulePlatform.Api/wwwroot (${files.length} entries)`);
console.log(`  federation entry  ${files.includes('remoteEntry.json') ? 'remoteEntry.json ✓' : 'MISSING ✗'}`);
console.log(`  index             ${files.includes('index.html') ? 'index.html ✓' : 'MISSING ✗'}`);
