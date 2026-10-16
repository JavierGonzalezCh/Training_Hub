// A6: ubicación del navegador para el check-in con el QR fijo. Devuelve { latitude, longitude } o null si el
// miembro niega el permiso, no hay GPS o se agota el tiempo. Nunca lanza.
window.gsmGeolocation = {
    get: function (timeoutMs) {
        return new Promise(function (resolve) {
            if (!navigator.geolocation) {
                resolve(null);
                return;
            }

            navigator.geolocation.getCurrentPosition(
                function (position) {
                    resolve({ latitude: position.coords.latitude, longitude: position.coords.longitude });
                },
                function () { resolve(null); },
                { enableHighAccuracy: true, timeout: timeoutMs || 10000, maximumAge: 30000 });
        });
    }
};
