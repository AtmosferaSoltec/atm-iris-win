# 08 · Tiempos

## Objetivo

Contrato §14. Los registros del cronómetro llegan al servidor (aunque el servicio termine sin
conexión), la pantalla Tiempos lee los de toda la iglesia y los ajustes se envían con permiso.

## Dependencias

Fases 02, 03 y 04.

## Pasos

1. `LiveTimeRecordRepository`:
   - Lista desde la copia local (más reciente primero).
   - `SaveAsync` → local + `PUT /service-records/:id` en la cola. El id lo genera el `BlockTimer` (`Guid`) y debe ser estable entre
     reintentos ("Reintentar" no crea otro).
   - Ajustar duración y cambiar responsable → local (estado `adjusted`, `personName` actual) + `PATCH /service-records/:id/blocks/:blockId`;
     eliminar → local + `DELETE`.
2. `BlockTimer.Record(...)` arma el registro con `serviceTypeName` (copia del nombre) y bloques `completed`/`skipped` con el
   `personName` del momento (spec §7.9).
3. "Guardar en la plantilla" (con `serviceTypes.manage`): `IServiceTypeRepository.SaveAsync` por la cola. Sin permiso, solo "Solo hoy".
4. Estado "Terminado" de la consola: si el registro sigue en la cola, "Se enviará cuando haya conexión" (ámbar) en lugar de error.
5. `TimeStatistics` y `TimesViewModel`: periodos por mes calendario en la zona de la iglesia (`ChurchClock`), incluido "Elegir mes…".
6. Nombres: persona borrada → `personName` guardado o "Persona eliminada"; tipo borrado → `serviceTypeName` guardado.
7. API falsa: `/service-records` (GET paginado, `PUT` idempotente con las reglas de permisos, `PATCH` de bloque, `DELETE`) y en sync.

## Criterios de aceptación

- Modo Fake: un servicio terminado con "Simular sin conexión" llega a la API falsa al reconectar, una sola vez; `operador@…` no
  ve los ajustes.
- Build limpio.

## Desviaciones

- `ITimeRecordRepository` suma `IsPendingAsync(id)` para el aviso ámbar "Se enviará cuando haya conexión" de la barra del cronómetro (se actualiza cuando la cola se vacía). `BlockTimer.Record(...)` recibe además el nombre del servicio (`serviceTypeName`) y el id del registro se genera una sola vez al terminar, así "Reintentar" nunca crea otro.
- Ajustar un registro desde Tiempos sigue guardando el registro completo en el ViewModel; el repositorio compara con la copia local y envía un `PATCH` por cada bloque cuyo tiempo o responsable cambió (dos si cambiaron ambos), no un `PUT` que reemplace todo.
- Las fechas de los registros viajan en UTC y en la app son hora local de la iglesia (`Mapping`), por lo que un registro de las 23:30 del último día del mes en Lima cae en ese mes.
- La API falsa recalcula `blockCount` de las personas cada vez que un registro cambia (y sube su versión para que llegue por sincronización), como pide el contrato §8.
- `PUT /service-records/:id` en la API falsa: crea con `records.write`; repetir el mismo contenido responde 200 sin permisos especiales (reintento); contenido distinto sobre uno existente exige `records.manage`.
- Una persona borrada se muestra con el nombre guardado en el bloque o "Persona eliminada"; un servicio borrado, con `serviceTypeName` o "Servicio eliminado".
