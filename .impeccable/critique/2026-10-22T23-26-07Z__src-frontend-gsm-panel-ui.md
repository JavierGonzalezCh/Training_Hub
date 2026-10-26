---
target: panel del Owner (todos los modulos)
total_score: 24
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 22
target_identity: "file:C:\\Proyectos\\Gymbro\\gsm\\src\\Frontend\\GSM.Panel.UI"
timestamp: 2026-10-22T23-26-07Z
slug: src-frontend-gsm-panel-ui
closed: true
---
# Crítica: panel del Owner (TrainingHub OS), 2026-10-22

Método: dual-agent (A en 3 grupos de módulos + B detector/navegador, aislados).

## Puntuación de salud (Nielsen)
| # | Heurística | Nota | Hallazgo clave |
|---|---|---|---|
| 1 | Visibilidad del estado | 3 | Buen veredicto en recepción; el dashboard queda vacío si falla la carga |
| 2 | Lenguaje del usuario | 3 | Español del gym; se cuelan "asiento inverso", hex, geocerca, EQUIPO/EQUIPOS |
| 3 | Control y libertad | 2 | No hay aviso de cambios sin guardar en ninguna pantalla; las pestañas de Configuración descartan ediciones |
| 4 | Consistencia | 2 | Cancelar/Volver, title/aria-label/nada, tablas apilables solo en algunos módulos |
| 5 | Prevención de errores | 2 | Cierre de caja precargado; push importante sin confirmar; traspaso sin fricción |
| 6 | Reconocer antes que recordar | 2 | Pagos sin miembro; 11 pestañas de ajustes sin buscador |
| 7 | Flexibilidad y eficiencia | 2 | Filas sin href; sin rangos rápidos ni atajos |
| 8 | Estética y minimalismo | 2 | Dashboard redundante; filtros que llenan el celular; ficha con 9 acciones |
| 9 | Recuperación de errores | 3 | Mensajes claros con traceCode; validaciones tardías por snackbar |
| 10 | Ayuda y documentación | 3 | Buenos textos de ayuda en contexto |
| Total | | 24/40 | Aceptable |

## Problemas prioritarios
1. [P0] Cierre de caja con el conteo precargado con lo esperado (CloseCashDialog.razor:55).
2. [P1] Publicar sin email (incluido el push "importante" a todos) no pide confirmación (AnnouncementActions.cs:16).
3. [P1] Configuración: 11 pestañas, inusables en celular, pierden cambios al cambiar de pestaña, errores en rojo al abrir.
4. [P1] Pagos sin miembro en la lista ni en el diálogo de anulación.
5. [P1] Sin anillo de foco visible en botones MudBlazor (reset del framework gana a :focus-visible).

## Por módulo (resumen)
- Dashboard: carga fallida = página vacía (P1); cifras de vencidos que no cuadran 16 vs 14 (P1); "Por vencer" 4 veces (P2).
- Recepción: buscador a y=658/812 en celular (P1); tarjeta apila hasta 9 avisos (P1); "Renovar y cobrar" abre "Asignar membresía" (P2); motivo de "Dejar pasar" validado tarde (P2).
- Miembros: filas sin href (P1); 6 filtros (P1); ficha con 9 acciones de igual peso y destructivas junto a cobrar (P1); teléfonos sin formato (P2).
- Planes/membresías: dos "Cancelar" opuestos en el diálogo de asignar (P1); tope de precio sin explicación (P2); jerga contable al cancelar (P2).
- Pagos y caja: conteo precargado (P0); lista sin miembro (P1); anulación sin identificar el pago (P1); anular = icono 26px (P2); "Caja abierta por TrainingHub" (P2).
- Comunicaciones: push importante sin confirmación (P1); conteo de destinatarios solo para email (P2); audiencia sin decir Y/O (P2); editor con ~9 decisiones (P2).
- Clases: botones de icono sin nombre accesible (P1); tabla de 7 columnas sin vista celular (P1); asistencia en tabla dentro de diálogo (P2); "Cancelar sesión" junto a Cerrar (P2).
- Equipos: el más cuidado; 5 filtros con un select de 17 opciones (P2); bandeja de fallas sin contador (P2); 4 iconos por fila con title (P2).
- Entrenamiento: filtros llenan el celular, primer ejercicio en y=641 (P1); enlaces sin href (P1); editor de plantillas sin aviso de cambios sin guardar (P1); "Marcar revisada" sin abrir la rutina (P2).
- Configuración: 11 pestañas (P1); EQUIPO vs EQUIPOS (P1); inusable en celular (P1); pierde cambios (P1); errores al abrir (P1); traspaso sin fricción (P1); suscripción duplicada (P2).
- Navegación: Configuración al final de "Más" (P2); Equipos dentro de Entrenamiento (P2); menú de usuario mezcla cuenta y avisos (P2); 404 con diseño de login (P3).
- Onboarding [código]: hereda errores en rojo y jerga de Datos del gym (P1); sin progreso de obligatorios (P2).

## Evidencia del detector
- CLI sobre Razor: no analiza el markup (resultado vacío no fiable). CSS: 8 side-tab (5 de estado, 3 decorativos), 1 layout-transition, 1 grid de fondo.
- Overlay inyectado en 5 páginas: logo "OS" 9.6px, transition de margin, border-accent-on-rounded en el dashboard, desequilibrio de columnas en /settings.
- DOM: sin desbordes; la mayoría de controles miden 32-38px de alto en celular; "HUB" del logo 2.73:1; /login con H1 vacío y salto H2→H6; páginas con un solo H1 y sin H2 en secciones.

## Preguntas
- Si el sistema sabe cuánto debería haber en caja, ¿por qué se lo muestra a quien cuenta antes de contar?
- Si el Owner solo viera 5 ajustes, ¿cuáles serían?
- ¿Por qué traspasar el gym cuesta lo mismo que cambiar el color primario?
