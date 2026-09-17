#!/usr/bin/env node
/**
 * Packages a built Native Federation remote into an installable module ZIP.
 *
 *   node tools/pack-module.mjs <project> [--version 1.1.0] [--out dist/packages]
 *
 * Package layout (the contract the platform validates):
 *
 *   <name>-<version>.zip
 *   ├── module.json          metadata + entry declaration
 *   └── federation/          verbatim contents of dist/<project>/browser
 *       ├── remoteEntry.json the Native Federation entry artifact
 *       ├── <Exposed>-<hash>.js
 *       ├── chunk-<hash>.js  lazy chunks, imported RELATIVE to remoteEntry.json
 *       ├── <shared>.js      shared dependency bundles
 *       └── styles/assets
 *
 * Nothing is rewritten on the way in. Native Federation emits only relative
 * imports and resolves a remote's base URL from the directory its
 * remoteEntry.json was fetched from, so the same bytes work unchanged under
 * /modules/<name>/<version>/ without any publicPath or base-href configuration.
 */

import { createWriteStream } from 'node:fs';
import { readFile, mkdir, readdir, stat } from 'node:fs/promises';
import { join, resolve, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ZipArchive } from 'archiver';

const workspaceRoot = resolve(fileURLToPath(new URL('.', import.meta.url)), '..');

function parseArgs(argv) {
  const [project, ...rest] = argv;
  if (!project) {
    console.error('usage: node tools/pack-module.mjs <project> [--version x.y.z] [--out dir]');
    process.exit(2);
  }

  const opts = { project, version: null, out: 'dist/packages' };
  for (let i = 0; i < rest.length; i += 2) {
    if (rest[i] === '--version') opts.version = rest[i + 1];
    else if (rest[i] === '--out') opts.out = rest[i + 1];
    else {
      console.error(`unknown option: ${rest[i]}`);
      process.exit(2);
    }
  }
  return opts;
}

async function walk(dir, base = dir) {
  const out = [];
  for (const name of await readdir(dir)) {
    const full = join(dir, name);
    const s = await stat(full);
    if (s.isDirectory()) out.push(...(await walk(full, base)));
    else out.push({ full, rel: relative(base, full).split(sep).join('/'), size: s.size });
  }
  return out;
}

const { project, version: versionOverride, out } = parseArgs(process.argv.slice(2));

const browserDir = join(workspaceRoot, 'dist', project, 'browser');
const manifestPath = join(workspaceRoot, 'projects', project, 'module.json');

// --- read and validate the module manifest --------------------------------
let manifest;
try {
  manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
} catch (err) {
  console.error(`✗ cannot read ${relative(workspaceRoot, manifestPath)}: ${err.message}`);
  process.exit(1);
}

if (versionOverride) manifest.version = versionOverride;

if (!/^[a-z][a-z0-9-]{1,63}$/.test(manifest.name)) {
  console.error(`✗ invalid module name "${manifest.name}" (lowercase kebab-case)`);
  process.exit(1);
}
if (!/^\d+\.\d+\.\d+(?:-[\w.-]+)?(?:\+[\w.-]+)?$/.test(manifest.version)) {
  console.error(`✗ invalid SemVer version "${manifest.version}"`);
  process.exit(1);
}

// --- verify the build output ----------------------------------------------
let files;
try {
  files = await walk(browserDir);
} catch {
  console.error(`✗ no build output at dist/${project}/browser — run: ng build ${project}`);
  process.exit(1);
}

const entryFile = files.find((f) => f.rel === manifest.entry);
if (!entryFile) {
  console.error(
    `✗ module.json declares entry "${manifest.entry}" but the build did not produce it.\n` +
      `  Present at the root: ${files.filter((f) => !f.rel.includes('/')).map((f) => f.rel).join(', ')}`,
  );
  process.exit(1);
}

// Cross-check the federation entry the same way the server will, so packaging
// fails at the developer's desk rather than at upload time.
const entry = JSON.parse(await readFile(entryFile.full, 'utf8'));

if (entry.name !== manifest.name) {
  console.error(
    `✗ name mismatch: module.json says "${manifest.name}", ` +
      `${manifest.entry} says "${entry.name}". Fix "name" in federation.config.js.`,
  );
  process.exit(1);
}

const exposedKeys = (entry.exposes ?? []).map((e) => e.key);
if (!exposedKeys.includes(manifest.routesExposedModule)) {
  console.error(
    `✗ module.json declares routesExposedModule "${manifest.routesExposedModule}" ` +
      `but the remote exposes [${exposedKeys.join(', ')}]`,
  );
  process.exit(1);
}

const present = new Set(files.map((f) => f.rel));
const referenced = [
  ...(entry.exposes ?? []).map((e) => e.outFileName),
  ...(entry.shared ?? []).map((s) => s.outFileName),
].filter(Boolean);

const missing = referenced.filter((f) => !present.has(f));
if (missing.length) {
  console.error(`✗ ${manifest.entry} references files missing from the build:\n  ${missing.join('\n  ')}`);
  process.exit(1);
}

// --- write the ZIP ---------------------------------------------------------
const outDir = resolve(workspaceRoot, out);
await mkdir(outDir, { recursive: true });

const zipName = `${manifest.name}-${manifest.version}.zip`;
const zipPath = join(outDir, zipName);

const output = createWriteStream(zipPath);
const archive = new ZipArchive({ zlib: { level: 9 } });

const done = new Promise((res, rej) => {
  output.on('close', res);
  archive.on('error', rej);
  archive.on('warning', (w) => console.warn(`  warning: ${w.message}`));
});

archive.pipe(output);
archive.append(JSON.stringify(manifest, null, 2) + '\n', { name: 'module.json' });
// Everything the Angular build emitted, under federation/ — no filtering, so
// assets and lazy chunks travel with the code that imports them.
archive.directory(browserDir, 'federation');
await archive.finalize();
await done;

const totalBytes = files.reduce((n, f) => n + f.size, 0);

console.log(`✓ ${zipName}`);
console.log(`  module        ${manifest.name} ${manifest.version} (${manifest.displayName})`);
console.log(`  entry         federation/${manifest.entry}`);
console.log(`  exposes       ${exposedKeys.join(', ')}`);
console.log(`  shared        ${(entry.shared ?? []).length} packages`);
console.log(`  artifacts     ${files.length} files, ${(totalBytes / 1024).toFixed(0)} KB raw`);
console.log(`  package       ${(archive.pointer() / 1024).toFixed(0)} KB → ${relative(workspaceRoot, zipPath)}`);
