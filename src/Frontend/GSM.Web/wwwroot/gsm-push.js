// Push web de GSM (N2): permiso de notificaciones, service worker y token de Firebase Cloud Messaging.
// Lo usa BrowserPushNotifications por JS interop. El SDK de Firebase se carga solo cuando la persona activa
// los avisos (import dinámico), así que sin Firebase configurado esta página no descarga nada extra.
//
// SIN VERIFICAR contra Firebase real: todavía no hay proyecto. Los mensajes del servidor son "data-only" y los
// muestra service-worker.js, por eso aquí no hace falta registrar un service worker de Firebase aparte.
window.gsmPush = (function () {
    const DISMISS_KEY = 'gsm.push.dismissed';
    const TOKEN_KEY = 'gsm.push.token';
    const FIREBASE = 'https://www.gstatic.com/firebasejs/11.0.2';
    let messaging = null;

    const isIos = () => /iphone|ipad|ipod/i.test(navigator.userAgent)
        || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    const isStandalone = () => window.matchMedia('(display-mode: standalone)').matches
        || navigator.standalone === true;
    const supported = () => 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;

    function state() {
        // iPhone: solo hay push con la web instalada en la pantalla de inicio (iOS 16.4 o superior).
        if (isIos() && !isStandalone()) return 'needs-install';
        if (!supported()) return 'unsupported';
        return Notification.permission; // 'default' | 'granted' | 'denied'
    }

    async function firebaseMessaging(config) {
        if (messaging) return messaging;
        const { initializeApp, getApps } = await import(`${FIREBASE}/firebase-app.js`);
        const { getMessaging } = await import(`${FIREBASE}/firebase-messaging.js`);
        const app = getApps().length
            ? getApps()[0]
            : initializeApp({
                apiKey: config.apiKey,
                projectId: config.projectId,
                appId: config.appId,
                messagingSenderId: config.messagingSenderId,
            });
        messaging = getMessaging(app);
        return messaging;
    }

    async function fetchToken(config) {
        const registration = await navigator.serviceWorker.ready;
        const { getToken } = await import(`${FIREBASE}/firebase-messaging.js`);
        const token = await getToken(await firebaseMessaging(config), {
            vapidKey: config.vapidKey,
            serviceWorkerRegistration: registration,
        });
        if (token) {
            try { localStorage.setItem(TOKEN_KEY, token); } catch { /* sin almacenamiento */ }
        }
        return token || null;
    }

    return {
        state,
        isInstalled: () => isStandalone(),

        // Pide el permiso (solo si aún no se dio) y devuelve el token; null si lo negó o falló.
        async requestToken(config) {
            const current = state();
            if (current === 'unsupported' || current === 'needs-install' || current === 'denied') return null;
            if (current === 'default') {
                const result = await Notification.requestPermission();
                if (result !== 'granted') return null;
            }
            return fetchToken(config);
        },

        // El token vigente sin pedir permiso.
        async getToken(config) {
            if (state() !== 'granted') return null;
            return fetchToken(config);
        },

        async forgetToken() {
            try {
                if (messaging) {
                    const { deleteToken } = await import(`${FIREBASE}/firebase-messaging.js`);
                    await deleteToken(messaging);
                }
            } catch { /* el token caduca solo */ }
            try { localStorage.removeItem(TOKEN_KEY); } catch { /* sin almacenamiento */ }
        },

        isDismissed() {
            try { return localStorage.getItem(DISMISS_KEY) === '1'; } catch { return false; }
        },

        dismiss() {
            try { localStorage.setItem(DISMISS_KEY, '1'); } catch { /* sin almacenamiento */ }
        },
    };
})();
