// Identidad visual de GSM: un solo tema, Forja. Se carga en <head>, antes que Blazor, para que el
// CSS (que selecciona sus tokens con data-gsm-theme) salga bien desde el primer pintado.
document.documentElement.setAttribute('data-gsm-theme', 'forja');

// Datos del navegador para el reporte de errores del cliente (logs.client_errors).
window.gsmClientInfo = { userAgent: function () { return navigator.userAgent; } };
