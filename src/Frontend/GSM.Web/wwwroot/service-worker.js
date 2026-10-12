// GSM no tiene modo offline: todo sale de la API. Este service worker existe para que el navegador ofrezca
// "instalar" la PWA y para recibir push (N2); no cachea nada ni intercepta peticiones.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

// Push de Firebase Cloud Messaging. El servidor envía mensajes "data-only" (title, body, link, notificationId),
// así que el navegador no muestra nada solo: lo mostramos aquí.
self.addEventListener('push', (event) => {
    let data = {};
    try {
        const payload = event.data ? event.data.json() : {};
        data = payload.data || payload.notification || payload;
    } catch {
        data = {};
    }

    const title = data.title || 'GSM';
    event.waitUntil(self.registration.showNotification(title, {
        body: data.body || '',
        icon: 'icon-192.png',
        badge: 'icon-192.png',
        tag: data.notificationId || undefined,
        data: { link: data.link || '/' },
    }));
});

// Tocar el aviso abre (o enfoca) la app en la ruta del aviso.
self.addEventListener('notificationclick', (event) => {
    event.notification.close();
    const link = (event.notification.data && event.notification.data.link) || '/';
    event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windows) => {
        for (const client of windows) {
            if ('focus' in client) {
                return client.navigate(link).then((c) => (c || client).focus()).catch(() => client.focus());
            }
        }

        return self.clients.openWindow(link);
    }));
});
