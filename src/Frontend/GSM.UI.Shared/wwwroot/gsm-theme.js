// Identidad visual de TrainingHub (codename GSM): un solo tema, Forja. Se carga en <head>, antes que Blazor, para que el
// CSS (que selecciona sus tokens con data-gsm-theme) salga bien desde el primer pintado.
document.documentElement.setAttribute('data-gsm-theme', 'forja');

// Datos del navegador para el reporte de errores del cliente (logs.client_errors).
window.gsmClientInfo = { userAgent: function () { return navigator.userAgent; } };

// El panel solo refresca el aforo y la caja mientras la página está visible (Dashboards §4).
window.gsmVisibility = { isHidden: function () { return document.hidden === true; } };
