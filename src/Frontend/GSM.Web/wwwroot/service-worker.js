// GSM no tiene modo offline: todo sale de la API. Este service worker solo existe para que
// el navegador ofrezca "instalar" la PWA; no cachea nada ni intercepta peticiones.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));
