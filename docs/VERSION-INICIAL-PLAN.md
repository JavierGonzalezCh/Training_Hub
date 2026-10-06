# Versión inicial — plan de huecos

Origen: recorrido de punta a punta en el navegador (2026-10-06) sobre la base `gsm` de desarrollo:
alta de gym por SuperAdmin → configuración del Owner → staff → socio → membresía → pago →
invitación → registro en `/app` → check-in con el QR del kiosco → logros, racha, novedades, clases
y rutina → cierre de caja.

**El camino feliz funciona de punta a punta.** Los huecos están en lo que rodea a ese camino:
corregir errores, comunicarse con las personas y la cara del socio. "Versión inicial completamente
funcional" = este guion corre sin tropiezos **y** se pueden deshacer los errores normales de
recepción. Sin eso no se despliega (ver `DEPLOY-DOKPLOY-PLAN.md`).

Igual que el resto del proyecto: **una sub-etapa por vez, con confirmación entre una y otra.**

---

## 1. Correcciones de caja y membresía (prioridad alta) — en curso

Hoy un cobro mal digitado o un plan mal asignado no tienen arreglo desde la UI, y la caja queda
descuadrada.

El dominio de Payments ya prevé la corrección: los pagos son inmutables y un ajuste es otro
`Payment` con monto negativo. Lo que falta es un camino guiado y seguro para hacerlo.

| Sub-etapa | Qué | Estado |
|---|---|---|
| **1.1** | **Anular un pago**: `POST /api/payments/{id}/void` crea el asiento inverso (monto negativo, mismo método, miembro y membresía) con `reverses_payment_id` y un motivo obligatorio. No se puede anular dos veces (índice único) ni anular una anulación. La respuesta de los pagos trae `isReversed`. UI: acción "Anular" en Pagos, cierre de caja y ficha del socio. | hecho |
| 1.2 | **Pago enlazado a la membresía**: "Asignar membresía" ofrece cobrar en el mismo paso, con el precio del plan precargado y `membershipId` enviado (el backend ya lo admite). El diálogo suelto de pago también permite elegir la membresía. | hecho |
| 1.3 | **Cancelar una membresía asignada por error** (estado `cancelled`, deja de cubrir fechas; si tiene pagos enlazados, avisa de que hay que anularlos). | pendiente |
| 1.4 | **Congelar / reanudar una membresía** (`Paused` ya existe en el enum pero no hay operación): extiende `EndDate` con los días congelados. | pendiente, decidir si entra en v1 |

## 2. Accesos que hoy dependen de copiar códigos a mano (prioridad alta)

- **Staff**: el correo trae un token de Identity de ~250 caracteres para pegar en
  `/reset-password`. Debe ser un enlace `/reset-password?email=…&code=…` que precargue el formulario.
- **Invitación del socio**: solo se muestra el código (`HX4MK3NG`). Falta enviarla por correo si el
  socio tiene email, botón de copiar y enlace `/app/registro?code=…` (también para WhatsApp).
- **R2 y Resend sin configurar**: sin correo real, lo anterior no llega a nadie. Configurar cuentas
  de prueba en local.
- El log de desarrollo escribe el token de restablecimiento completo: verificar que en producción
  el remitente real nunca lo loguee.

## 3. La cara del socio (prioridad alta)

- El socio entra por el login del panel ("Sistema de gestión · B2B SaaS — Tu operación, bajo
  control total"). Necesita un login propio en `/app` (con la marca del gym si se conoce).
- Tras registrarse no queda con la sesión iniciada; el registro pide un email que el gym ya tenía.
- No hay perfil en `/app`: editar teléfono o foto y cambiar contraseña con la sesión iniciada
  (no existe endpoint en `/api/me`). Lo mismo para staff y Owner.

## 4. Fricción (prioridad media)

- Gym nuevo sin guía: el dashboard sale en ceros. Lista de primeros pasos (planes, staff, QR).
- La ficha del gym en SuperAdmin no muestra al Owner ni permite reenviarle el acceso.
- Cierre de caja: muestra el concepto y no el socio; no se imprime ni exporta; no hay "cerrar caja".
- Clases: el instructor es texto libre, sin relación con el staff.
- Un socio recién creado aparece "ACTIVO" sin membresía: se confunden el estado del socio y el de
  su membresía en la lista.

## 5. Detalles

- Botones de icono sin nombre accesible ("Nuevo plan", "Nuevo miembro", acciones de la ficha).
- La columna "Días" de la cartera solo se llena en mora y el encabezado confunde; el seed promete
  un gym en mora y ninguno aparece así.
- El primer clic en "Nuevo gym" a veces no abre el diálogo y el formulario se corta hasta hacer scroll.

## No recorrido todavía

Rechazo por membresía vencida y días de gracia (cubiertos por tests de integración), crear una
clase desde la UI, asignar una rutina desde la ficha, kiosco a pantalla completa, anuncios y
reacciones.
