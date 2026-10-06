// Identidad visual de GSM: un solo tema, Forja. Se carga en <head>, antes que Blazor, para que el
// CSS (que selecciona sus tokens con data-gsm-theme) salga bien desde el primer pintado.
document.documentElement.setAttribute('data-gsm-theme', 'forja');
