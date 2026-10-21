# V1 — Recorrido de punta a punta

Recorrido en el navegador de la v2 (rama `v2`, base `gsm` en `V2_Q7`), con la API y la web locales.
Los defectos están en `docs/BUGS.md` (B7–B16). Aquí queda lo que se probó, lo que funcionó, las
mejoras que no son defectos y lo que falta recorrer.

## Sesión 1 — 2026-10-20

Escritorio (~1000 px) y celular (375 px). Gym nuevo **Hierro V1 Gym** (`hierro-v1`, Owner
`owner@hierro.local`, recepcionista `recepcion@hierro.local`, miembro de prueba Andrea Mejía, ya
anonimizada) y **Gimnasio Demo** del seed. Contraseñas: las de `scripts/seed-demo.ps1`.

### Funcionó

| Módulo | Qué se probó |
|---|---|
| SuperAdmin | Dashboard (las cifras cuadran con los gyms), cartera, alta de gym con prueba de 30 días, owner pendiente, copiar enlace, impersonación en escritura con motivo (la nota queda firmada con el nombre de la plataforma y la acción en auditoría), finalizar, planes, catálogos, cobros, errores (ninguno nuevo en la sesión). |
| Gyms (G1, G5) | El enlace de activación entra directo al onboarding. Ubicación, horario con plantilla (festivos de Colombia 2026 correctos, "abierto · cierra 22:00" correcto), máquinas del catálogo, planes por plantilla, cierre con "Para completar". |
| Identity / staff | Alta de staff con perfil y correo, permisos, el backend impide quitar al último Owner (409 con buen mensaje). |
| Configuración | Las 11 pestañas cargan sin errores. |
| Members | Alta (la lista muestra "SIN PLAN"), ficha completa, invitación, autorizaciones (aceptar y revocar), anonimización (los datos se borran y lo financiero se conserva en la base). |
| Memberships | Asignar y cobrar en un paso (fechas 20 oct → 19 nov), plan de grupo con beneficiario y, si el cobro falla, "Cancelar el grupo" deja las dos membresías `cancelled` con motivo. |
| Payments (P1–P3) | Métodos de Colombia, abrir caja con base, cobro suelto, cerrar con arqueo (descuadre con nota obligatoria), reporte de turno, anular un pago (asiento inverso, "no toca la caja"), cierre diario. |
| Attendance | Kiosco: alta con código de 8 caracteres, emparejar, QR rotativo de 30 s. Check-in del miembro por el enlace del QR, con logro "Primera visita". Recepción: "Dejar pasar" a una vencida con motivo. Asistencia lista la entrada. |
| Dashboard (D1) | Demo: al día + por vencer + en gracia + vencidos + sin plan = activos. Los ingresos cuadran con SuperAdmin. |
| App del miembro (D2) | Login con la marca del gym, Inicio, perfil, gimnasio, avisos, contraseña/correo, membresía, novedades, autorizaciones. |
| Classes | Clase recurrente, aviso "Nueva clase" en Novedades, reserva desde la app. |
| Routines | Rutina desde la ficha, búsqueda en el catálogo, aviso de equipo faltante (pide rack), rutina en la app, registro de series. |
| Communications | Publicar un anuncio. |

### Mejoras (no son defectos)

- **Marca:** resuelto el 2026-10-20: el producto pasó a TrainingHub (pantalla de carga, correos, textos y firma del SuperAdmin). El 404 de `/app` muestra la marca del panel ("Sistema de gestión · B2B SaaS").
- **Cerrar caja:** "Efectivo contado" viene precargado con lo esperado y dice "la caja cuadra" antes
  de contar. Un arqueo a ciegas obliga a contar de verdad.
- **Cobro en efectivo con la caja cerrada:** "Asignar plan de grupo" (y es de suponer que "Asignar
  membresía") crea primero y cobra después, y queda en "el grupo quedó creado, pero el cobro no se
  registró". Hay salida (cancelar o reintentar), pero sería mejor avisar de la caja antes de crear.
- **Anular con la caja cerrada:** ofrece "Sí, se devuelve ahora (sale de la caja abierta)" aunque no
  hay caja abierta.
- **Fechas y horas:** la fecha de nacimiento no se puede escribir y el selector abre en el mes actual
  (hay que bajar año por año). Además, al intentar escribirla se traga lo que sigue. La hora de la
  clase tampoco se escribe, y el reloj queda recortado en pantallas bajas.
- **Onboarding:** "Siguiente" sin "Guardar cambios" no avanza y el aviso sale arriba, fuera de la
  vista. Lo esperable es guardar al avanzar. El paso Máquinas no muestra lo que ya se agregó. En el
  diálogo del catálogo, los campos Cantidad y Zona no tienen estilo. "Ver en el mapa" queda pegado al
  título "REDES Y CONTACTO".
- **Invitación del miembro:** el correo trae solo el código, sin enlace `/app/registro?code=…`, y el
  diálogo no ofrece copiar el enlace.
- **Pagos:** la lista y "Pagos del día" no muestran el miembro (pendiente desde la v1).
- **Controles nativos sin estilo:** "Cambiar entrenador" en la ficha y "Calendario" en el editor de
  rutinas.
- **Dashboard:** "Por vencer · 7 días" y "Por vencer · 7 días o menos" son dos tarjetas con el mismo
  dato.
- **SuperAdmin:** el banner de impersonación no dice qué gym. En la ficha de un gym en prueba, la
  tarjeta "Pagado hasta" muestra la fecha de fin de la prueba. En `/admin`, "Miembros activos"
  (estado del miembro) y "Miembros (límite)" (los que cuentan para el plan) se leen como lo mismo.
- **Planes:** la lista no indica cuáles son de grupo. Los botones "Editar" no dicen de qué plan (lector
  de pantalla).
- **Comunicaciones:** un anuncio vencido ("visible hasta el 8 oct") sigue como "Publicado".
- **Nombres en la app:** la pestaña "Progreso" abre `/app/logros`, titulada "Mi progreso", y
  `/app/progreso` se llama "Mis récords".

### No cubierto en esta sesión

- **Necesita cuentas o dispositivo:** Android (AP3+N3, AP5, A6, escáner), push, subida de archivos
  (R2) y correos reales (Resend).
- **Pendiente de recorrer:** login del staff con permisos limitados, cuenta con varios gyms
  (`select-gym`/`switch-gym`), congelar/reanudar y cancelar membresía, tiqueteras, lista de espera de
  clases, roster y asistencia a clase, entrada con documento en el kiosco, QR fijo con geocerca,
  reporte de fallas de máquinas, logros manuales/referidos/ranking, plantillas de rutina, "Mi
  progreso" con datos, preferencias de avisos, vistas de celular del panel (después de B7) y `/app` en
  escritorio (AP6).
