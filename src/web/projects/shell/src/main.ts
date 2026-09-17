import { initFederation } from '@angular-architects/native-federation';

/**
 * Initialise Native Federation with NO remotes.
 *
 * This is deliberate. A static `federation.manifest.json` would hardcode the
 * remote list into the Shell bundle, which is exactly what this platform
 * avoids. Passing `{}` registers only the *host's* shared dependencies and
 * installs the import map; remotes are registered later, at navigation time,
 * by `loadRemoteModule({ remoteEntry })` using URLs returned by
 * `GET /api/modules`.
 */
initFederation({})
  .then(() => import('./bootstrap'))
  .catch((err) => {
    console.error('[shell] federation init failed', err);
    document.body.innerHTML =
      '<div style="font:14px system-ui;padding:2rem;color:#991b1b">' +
      '<h1>Shell failed to start</h1>' +
      '<p>Native Federation could not be initialised. See the browser console.</p></div>';
  });
