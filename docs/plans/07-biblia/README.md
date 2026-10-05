# 07 · Biblia sin conexión

## Objetivo

Contrato §13. La Reina-Valera 1909 completa en la PC, descargada una vez y consultada sin conexión
desde el selector de Biblia que ya existe.

## Dependencias

Fase 01. Independiente de la sincronización.

## Pasos

1. `BibleStore` (`Core/Bible/`):
   - Descarga `GET /bible/translations/rvr1909/download` (activa `AutomaticDecompression = GZip` en el `HttpClientHandler` real) y
     guarda el JSON en `LocalFolder/Bible/rvr1909-v<version>.json` con su `ETag`.
   - Comprueba actualizaciones con `If-None-Match` como máximo una vez al día, tras una sincronización.
   - Carga perezosa en segundo plano a una estructura en memoria (libros + `string[][]` por libro) con `IrisJsonContext`.
2. `LiveBibleRepository` implementa `IBibleRepository` sobre `BibleStore` (nombre de la traducción, libros por testamento, número de
   versículos, versículos).
3. Sin descargar todavía: se descarga sola tras la primera sincronización si el módulo Biblia está encendido. Si el operador abre el
   selector antes: "Descargando la Biblia… {porcentaje}"; sin red: "Necesitas conexión para descargar la Biblia la primera vez." +
   "Reintentar".
4. Comportamiento de la spec §6.3 / §7.5 sin cambios (capítulo completo, ‹ › y flechas del teclado, pie "Juan 3:16").
5. API falsa: endpoints de §13. Para la descarga usa el índice de 66 libros de `SampleData` y los capítulos con texto real que ya hay
   (Salmos 23, Juan 1 y 3, Génesis 1), con el resto generado como en el mock ("Texto de ejemplo de {Libro} {cap}:{v}.").
   La Biblia completa real solo llega con la API verdadera.

## Criterios de aceptación

- Modo Fake: la descarga ocurre tras sincronizar; Juan 3:16 funciona con "Simular sin conexión".
- Build limpio.

## Desviaciones

- Los ids de libro pasan a ser los códigos USFM del contrato ("GEN", "JHN", "PSA"…) en modo Fake y Live; los ids cortos de la maqueta ("gn", "jn") quedan solo en modo Mock. Nada en la interfaz depende de ellos.
- La descompresión gzip se activa en el `SocketsHttpHandler` del transporte real (`AutomaticDecompression = GZip | Deflate`), no en un `HttpClientHandler`. La API falsa responde sin gzip.
- `IBibleRepository` suma `Status`, `StatusChanged` y `RetryAsync()` para que el selector muestre "Descargando la Biblia… N %", el mensaje sin conexión y "Reintentar". El porcentaje usa `sizeBytes` de `GET /bible/translations` (única fuente fiable cuando la respuesta viaja comprimida) y nunca pasa de 99 % hasta terminar.
- La Biblia se descarga sola tras cada ronda de sincronización terminada (`BibleSyncTask`) si el módulo Biblia está encendido; las comprobaciones de actualización con `If-None-Match` ocurren como máximo cada 24 h (se guarda la última en `rvr1909.meta.json`).
- Los archivos de la Biblia y de la caché de multimedia del modo Fake viven en una subcarpeta `fake/` para no mezclarse con los reales.
- La API falsa solo trae texto real de Salmos 23, Juan 1 y 3 y Génesis 1; el resto es "Texto de ejemplo de {Libro} {cap}:{v}.". La Biblia completa llega con la API verdadera.
