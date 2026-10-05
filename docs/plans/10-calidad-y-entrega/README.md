# 10 · Calidad y entrega

## Objetivo

Cerrar Windows: pruebas de lo importante, limpieza, documentación, instrucciones de integración y el
reporte final.

## 1. Pruebas (xUnit, `Tests/`)

`Tests/` enlaza fuentes sin dependencias de WinUI. Mueve a carpetas sin WinUI (o enlaza) lo que haga falta para probar:
- Decodificación de los ejemplos del contrato con `IrisJsonContext` (`AuthResult`, `SyncPage` con cambios y borrados, `ServiceType`,
  `ServiceRecord`, error con `errors`), fechas con milisegundos, enums desconocidos.
- `AuthSessionManager`/`AuthHandler`: refresh antes de vencer, single flight concurrente, 401 en refresh cierra sesión, red no
  (con un `HttpMessageHandler` de prueba).
- `SyncEngine`: páginas en orden, `hasMore`, cursor guardado, borrados, pausa durante el servicio (con `LocalStore` sobre SQLite en memoria).
- `Outbox`: orden, reintento sin red, descarte con 4xx, ids estables.
- Repositorios Live de personas y tipos de servicio: duplicados por `NameKey`, borrar persona limpia plantillas.
- `TimeStatistics` con la zona de la iglesia (registro a las 23:30 del último día del mes en Lima cae en ese mes).
- `NameKey` con espacios internos (contrato §2).
- La propia API falsa contra el contrato (auth, gracia y reuso del refresh, sync incremental).

`dotnet build Iris.csproj -p:Platform=x64`, `dotnet build Iris.csproj -c Release -p:Platform=x64` (para detectar problemas de recorte)
y `dotnet test Tests/Iris.Tests.csproj` en verde.

## 2. Limpieza

- Por defecto: `Fake` en Debug, `Live` en Release. Mocks solo para diseño y pruebas.
- Sin código sin uso de la maqueta, sin `TODO` sin explicar, sin `Debug.WriteLine` sueltos (usa el logger).

## 3. Documentación

- `README.md` del repo (créalo): qué es, requisitos (Windows 11, .NET 8, Windows App SDK), modos Mock/Fake/Live y el diálogo
  Conexión (`Ctrl+Shift+F12`), arquitectura (`Core/Networking`, `Auth`, `Persistence`, `Sync`, `Media`, `Bible`), pruebas.
- **Integración con la API de la Mac** (para el revisor): cómo averiguar la IP de la Mac, poner `ApiBaseUrl = http://<IP>:3020/api/v1`
  y modo Live, probar con "Probar", y qué hacer si no conecta (firewall de macOS, misma red). Recuerda que las URLs de archivos dependen
  de `STORAGE_PUBLIC_ENDPOINT` en la API.
- Actualiza `CLAUDE.md` (arquitectura nueva y reglas de la API falsa).

## 4. Reporte

Entrega el reporte de `00-fundamentos/plataforma.md` §6 (incluye en "Pendiente para la integración" todo lo que solo se probó contra la
API falsa) y pregunta al usuario si puede dar el repo por terminado.

## Desviaciones

_(Completar al cerrar la fase.)_
