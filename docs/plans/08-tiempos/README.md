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

_(Completar al cerrar la fase.)_
