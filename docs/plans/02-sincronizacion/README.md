# 02 · Sincronización: copia local, feed y cola de escrituras

## Objetivo

Contrato §12. La PC trabaja **sin conexión**: todo lo que muestra sale de una copia local de la
iglesia, que se pone al día con `GET /sync/changes`, y todo lo que escribe va primero a la copia local
y a una **cola** (outbox) que se envía en orden cuando hay red.

## Dependencias

Fases 00 y 01.

## Diseño

```
Repositorios Live ──lee──▶ LocalStore (SQLite)  ◀──aplica páginas── SyncEngine ◀── GET /sync/changes
        │                         ▲                                         │
        └─escribe─▶ LocalStore + Outbox ─────────── flush en orden ─────────┘──▶ POST/PUT/PATCH/DELETE
```

### 1. `LocalStore` (`Core/Persistence/`)
- `Microsoft.Data.Sqlite` (sin EF: el recorte de Release lo rompería). Base en `LocalFolder/iris.db`, modo WAL.
- Esquema simple y estable: una tabla por entidad con `id TEXT PRIMARY KEY`, `church_id TEXT`, columnas de orden/búsqueda que se
  necesiten (`name_key`, `date`) y `payload TEXT` con el DTO completo en JSON (`IrisJsonContext`). Tablas: `church`, `people`,
  `service_types`, `songs`, `media`, `service_records`, `sync_state` (iglesia, cursor, última sincronización), `outbox`.
- Migraciones con `PRAGMA user_version` y un arreglo de scripts en código.
- Métodos de dominio que devuelven modelos de `Core/Models` (nunca DTO hacia los ViewModels): `GetPeopleAsync`, `UpsertAsync`,
  `DeleteAsync`, `ApplyAsync(SyncPage)` en **una** transacción, `ClearAsync`.
- Evento `Changed` (`StoreChangeKind`: People, ServiceTypes, …) en el hilo de interfaz (vía `DispatcherQueue`) para que los
  ViewModels abiertos recarguen en silencio.

### 2. `SyncEngine` (`Core/Sync/`)
- `SyncNowAsync(reason)`: si hay red y sesión, primero **vacía la cola** y luego pide páginas desde el cursor hasta
  `hasMore == false`, guardando el cursor tras cada página. Una ejecución a la vez (si llega otra, se encadena una más al terminar).
- Disparadores (contrato §12): al entrar con sesión, al volver al Inicio, cada 5 min con el Inicio visible (`DispatcherQueueTimer`),
  al recuperar la red y el botón "Actualizar". **Pausado mientras hay un servicio en curso** (`Suspend`/`Resume` desde
  `LiveConsoleViewModel`).
- `SyncStatus` observable: `Idle(lastSync)`, `Syncing`, `Offline(pending)`, `Failed(message)`.

### 3. Cola de escrituras (`Core/Sync/Outbox.cs`)
- Fila: `seq` (autoincremental), método, ruta, cuerpo JSON, `created_at`, intentos, último error.
- `EnqueueAsync` siempre después de aplicar el cambio en la copia local (escritura optimista).
- `FlushAsync`: en orden, una a la vez. 2xx → se borra. Red/5xx/429 → se detiene y reintenta con espera exponencial (5 s, 15 s,
  60 s, luego cada 5 min). 401 → lo resuelve `AuthHandler`; si la sesión murió, la cola queda para el próximo inicio de sesión de la
  misma iglesia. Otro 4xx → se descarta, se guarda el mensaje para avisar ("No se pudo guardar «…»: {mensaje}") y se fuerza una
  resincronización completa de ese tipo.
- Solo endpoints idempotentes del contrato (§2): crear con `id` propio, `PUT` para tipos de servicio, registros y módulos.

### 4. Conexión
- `ConnectivityMonitor` con `Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged` (+ el interruptor
  "Simular sin conexión" del modo Fake). Al volver la red: `SyncNowAsync(Reconnected)`.

### 5. API falsa
- Implementa `/sync/changes` (paginado por versión, borrados, `church` cuando cambia) y deja la base para los endpoints de
  escritura de las fases siguientes.

### 6. Interfaz
- `AppTopBar` (Inicio y pantallas de iglesia): indicador junto al estado del TV: "Actualizado hace 2 min" · "Actualizando…" ·
  "Sin conexión · 3 cambios pendientes" (ámbar) · error con "Reintentar". Clic → `Flyout` con detalle y "Actualizar ahora".
- Primera sincronización tras iniciar sesión: capa de carga sobre el Inicio "Preparando tu iglesia…" con progreso; sin red y sin
  copia: "Necesitas conexión para descargar tu iglesia la primera vez." + "Reintentar".
- Cerrar sesión o cambiar de iglesia con cola pendiente: confirmación "Hay {N} cambios sin enviar. Si sigues, se perderán." Luego
  `ClearAsync` y cola vacía.

## Criterios de aceptación

- Modo Fake: primera sincronización llena la copia; con "Simular sin conexión" la app abre con datos; una persona agregada sin red
  llega a la API falsa al reconectar, una sola vez.
- Build limpio.

## Desviaciones

_(Completar al cerrar la fase.)_
