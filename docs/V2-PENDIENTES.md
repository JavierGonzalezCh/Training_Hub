# v2 — Lo que falta para cerrar

> Estado al 2026-10-16. Rama `v2`. Bitácora detallada: `docs/V2-NOCHE.md` (estado por tarea, preguntas, registro).

## 1. Dónde estamos

Todas las sub-etapas del plan (`docs/V2-BUILD-PLAN.md`, olas 0 a 6) que se pueden escribir están **construidas, unidas y en verde**:

- **Suite:** 1171 tests de integración + 663 bUnit, todos pasan contra `gsm_test` (≈ 15–30 min la de integración).
- **Compila:** backend, `GSM.Frontend.slnx` y `GSM.Mobile` para Android (`dotnet build src/Frontend/GSM.Mobile -f net10.0-android -p:RuntimeIdentifier=android-arm64`).
- **Quedan sin hacer** solo las dos tareas de cierre que no dependen de código: **V1** (recorrido de punta a punta) y **DP** (despliegue).

Avance estimado: **~85 %**. El código está; falta verificarlo en condiciones reales y desplegarlo.

## 2. Lo que necesita al dueño (bloquea V1 y DP)

| Qué | Lo necesitan | Notas |
|---|---|---|
| Servidor con Dokploy (ambiente de pruebas y producción) | DP | Plan en `docs/DEPLOY-DOKPLOY-PLAN.md`. Configurar `ForwardedHeaders` detrás del proxy (si no, la IP de `logs.security_events` y del límite por IP es la del proxy). |
| Cuenta de Cloudflare R2 | Fotos de miembros, máquinas, ejercicios (R6), anuncios (CO4), iconos de logros | Hoy toda subida falla con 500 y la UI lo avisa. |
| Cuenta de Resend + dominio verificado | Todos los correos (invitaciones, código de acceso, avisos, alertas LG5) | Sin Resend los correos van al log. |
| Proyecto de Firebase por ambiente, con app Android `com.gsm.mobile` | Push web (N2), push Android (N3), alertas por push (LG5) | Llenar `Notifications:Fcm:*`. Android se inicializa por código desde `GET /api/push/config` (sin `google-services.json`). |
| Teléfono Android o emulador | AP3+N3, AP5, A6 (ubicación), escáner QR | Nada de `GSM.Mobile` se ha ejecutado nunca en un dispositivo. |
| Revisión legal (Ley 1581, Ley 2300) | M2 (texto de autorizaciones, hoy borrador), M3 (salud), M6 (anonimización), comunicaciones | |
| Imágenes de ejercicios y máquinas con licencia | Catálogo del sistema (S4, E1) | Hoy hay íconos por tipo. |
| Responder las preguntas abiertas | Ajustes finos | `docs/V2-NOCHE.md` §2: cada tarea dejó sus decisiones conservadoras. Ninguna bloquea. |

Claves de configuración que la API exige o usa en producción: `Checkin:SigningKey`, `Communications:UnsubscribeSigningKey`, `Communications:PublicApiBaseUrl`, `Jobs:Secret`, `Identity:PublicWebBaseUrl`, `Notifications:Fcm:*`, las de R2 y Resend. La tarea periódica `POST /internal/jobs/communications/run` debe programarse en Dokploy cada 5–10 min (ahora también corre avisos de suscripción, anonimización y alertas).

## 3. Lo que está construido pero sin verificar

- **Android (AP3+N3, AP5, A6):** compila; nunca se ha ejecutado. Paquetes nuevos sin probar en runtime: `Xamarin.Firebase.Messaging` 124.1.2.2 y `Plugin.Fingerprint` 2.1.5 (alternativa si falla: `Xamarin.AndroidX.Biometric`).
- **Vistas de celular de AP7** (planes, ejercicios, equipos): mirarlas a 375 px en el navegador.
- **Ninguna pantalla nueva de esta tanda se recorrió en el navegador**: los agentes solo escribieron código y los tests son bUnit. V1 debe cubrir sobre todo D1 (dashboard por bloques), D2 (Inicio del miembro), G5 (onboarding), MS7 (planes de grupo), M6 (anonimización) y "Mi progreso".
- **Base de desarrollo `gsm` atrasada:** está en `V2_P13_TrialsNotConverted`; `gsm_test` está al día (`V2_R11_RoutineTemplates`). Antes de usar la API en local: respaldar `gsm` (`pg_dump`) y migrarla con `dotnet ef database update --project src/Backend/GSM.Infrastructure --context GsmDbContext --connection "<cadena de gsm>"`. Varias migraciones no se revierten en datos (M6 rellena fechas de baja, R4, I2).

## 4. Seguimientos que dejaron las tareas (no bloquean)

- **AP6:** Rutina, Clases y Mi progreso funcionan en escritorio pero sin los dos paneles de la spec (queda la clase `.gsm-m-split` lista).
- **R6:** no hay "quitar imagen/video" sin reemplazar; vaciar la URL deja el archivo huérfano en R2.
- **AP3:** tocar un aviso de otro gym solo navega, no cambia de gym; el staff no tiene "olvidé mi contraseña" en la app.
- **LG5:** `/admin` aún no registra push del SuperAdmin; las alertas llegan solo por correo.
- **M6:** textos libres de otros módulos (motivos de "Dejar pasar", notas de pagos, motivos de cancelación) no se limpian al anonimizar; el staff aún puede añadir notas a una ficha anonimizada.
- **G6:** las secciones de Membresías, Rutinas y Equipos de `/settings` están a medio poblar (la gracia sigue en la pestaña Check-in).
- **D3:** el conteo de "Límites" hace una consulta por gym (N+1).
- **Patrón a revisar:** 5 handlers de ajustes perezosos (`CheckinSettings`, `CommunicationsSettings` ×2, `MembershipSettings`, `RoutineSettings`) añaden la entidad antes de validar. En producción no se nota (cada petición tiene su contexto); se corrigió solo en `MemberSettings`.
- **Plan §2.4:** los nombres de rama son `v2-<id>-<nombre>`, no `v2/...` (Git no lo admite con la rama `v2`).

## 5. Para cerrar la v2

1. Dueño: cuentas externas (§2) y respuestas a las preguntas de `V2-NOCHE.md` §2.
2. Migrar `gsm` (con respaldo) y levantar la API y la web en local.
3. **V1:** recorrer el guion de `docs/VERSION-INICIAL-PLAN.md` ampliado a la v2, en navegador (escritorio y 375 px) y en Android. Cada falla, a `docs/BUGS.md` con su test.
4. **DP:** desplegar con Dokploy (`docs/DEPLOY-DOKPLOY-PLAN.md`) con R2, Resend y Firebase configurados.
5. Unir `v2` a `main`.
