# Retroalimentación de producción

Observaciones del recorrido en producción. **Solo se identifican aquí**: nada se ha corregido ni planeado todavía.
Cada punto lleva la fecha en que se anotó y quién lo vio.

## 2026-11-08 · Javier (dueño), primer recorrido tras la depuración de producción

### Errores

- **E1 · `GET /api/exercises` responde 500.** También falla `GET /api/admin/catalog/exercises`. En `logs.errors`
  aparece `InvalidCastException: Reading as 'GSM.Domain.Routines.PowerLift' is not supported for fields having
  DataTypeName '-.-'`. El enum `routines.power_lift` existe en la base y está mapeado en `RoutinesEnums`.
  Lo más probable es que la API cargara los tipos de Postgres antes de que la migración creara el enum, y que se
  arregle reiniciando el contenedor. Falta revisar el orden "migrar → arrancar" del despliegue para que no vuelva a pasar.
  Posible relación con el error `5B07B0` del 7 nov (`GET /api/me/routines`), anotado en `V2-PENDIENTES.md`.
  **Confirmado:** reiniciar el contenedor lo resolvió. Quedaron 4 filas en `logs.errors` (13:12–13:51, hora
  Colombia), así que el registro de errores sí escribe en producción, a diferencia de lo que dice `V2-PENDIENTES.md`.
- **E2 · El tipo de negocio no coincide en la ficha del SuperAdmin.** En la ficha de "Personal Training Test 1", el
  selector «Tipo de negocio» muestra *Gimnasio*, pero en la base `gyms.gyms.type = personal_trainer`.
  Hay que confirmar si el selector no carga el valor guardado.
- **E3 · Subir la foto de perfil falla** (lo encontró Daniela, app del miembro). **No es R2.** En
  `logs.business_rejections` hay 3 intentos de `POST /api/me/photo` (14:51, 14:54 y 15:00, hora Colombia) que
  respondieron **400 «Solicitud inválida»** sin `detail` ni `field_errors`, es decir, sin validación del modelo ni
  excepción de dominio. La petición se rechaza antes de llegar al caso de uso. Sin reproducir; sospechas: el proxy
  de Dokploy, un límite del multipart, o el nombre o formato del archivo que manda el celular (p. ej., HEIC).
  Trazas: `dc31cacc85a24b3d272632b0d50b5e6c`, `d775ca4b451d126e5b29a88d54c9f195`, `9bcd3c3f452420eb1b4b1cec64a516b7`.
  Además, el mensaje que ve el usuario no explica nada. **Daniela usa iPhone**: refuerza la sospecha de HEIC o de cómo
  Safari arma el multipart (nombre como `image.jpg` o tipo de contenido vacío). Para reproducirlo hay que subir desde
  Safari en iOS, con una foto de la cámara y otra de la galería.
  **Corrección aplicada (sin desplegar ni verificar en iPhone):** `GSM.UI.Shared/Uploads/PhotoUpload` hace que el
  navegador convierta la foto a JPEG de hasta 1600 px (`RequestImageFileAsync`) y la lea entera antes de enviarla.
  Lo usan la foto del miembro en la app, la foto que el staff pone en la ficha y la foto del reporte de equipo. Si
  la lectura falla, se muestra un aviso claro en vez del 400. Los logos y los íconos no lo usan, porque el JPEG
  pierde la transparencia. Tests: `PhotoUploadTests`.
- **E4 · Resend está en modo de pruebas.** `logs.external_failures` registra 403 de Resend en `send_email`: «You can
  only send testing emails to your own email address… verify a domain». La clave existe, pero falta verificar el
  dominio y cambiar el remitente. Hasta entonces, los correos solo llegan a la cuenta dueña de Resend.

### Alta de miembros (panel del gym)

- **M1 · La fecha al registrar un miembro** (fecha de nacimiento) es incómoda de llenar. Falta detallar qué falla
  exactamente (formato, selector de calendario o año por defecto).
- **M2 · Celulares internacionales.** Hoy solo se aceptan celulares colombianos de 10 dígitos, sin +57 (FL1).
- **M3 · Pedir los datos médicos en el alta.** El alta la hace el entrenador, que puede resolver dudas en el momento;
  si queda para después, el miembro puede no llenarlos nunca.

### Panel del dueño del gym

- **O1 · El onboarding no es claro.** Falta revisar en detalle los pasos de Máquinas y siguientes.
- **O2 · Mucho texto y pocas imágenes o ayudas visuales.** Hay que mejorar la experiencia del dueño en general.
- **O3 · «Ver la configuración recomendada» es enredada.** Se quiere:
  - una lista de toggles, prendidos o apagados según la recomendación del tipo de negocio;
  - en cada toggle, un ícono de información que, al pasar el mouse, explique qué pasa al prenderlo o apagarlo;
  - un solo botón «Guardar» al final.
- **O4 · Dónde se activa que el miembro cree su propia rutina.** El dueño no la encontró al configurar
  (`MembersCanCreateRoutines` quedó apagada en el gym del entrenador personal).

- **O5 · El color secundario se pide pero no se usa.** En Ajustes → Datos del gym (`GymDataTab.razor`) el owner
  elige un color secundario, pero ninguna otra pantalla lo lee (es el único `.razor` que lo menciona). O se usa en
  algún lado (app del miembro, PWA, correos) o se quita del formulario.

### Panel del SuperAdmin (`/admin`)

- **S1 · Muy oscuro y con mucho texto.** Son los mismos problemas que en el panel del dueño: faltan imágenes y ayudas visuales.
- **S2 · Onboarding pendiente arriba de la ficha del gym.** Hoy aparece al final de la ficha. Si el onboarding está
  pendiente, debe verse arriba, para contactar al dueño y preguntarle qué pasó.
