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

- Por defecto: `Live` contra el servidor desplegado (Fake y Mock se eligen en Conexión). Mocks solo para diseño y pruebas.
- Sin código sin uso de la maqueta, sin `TODO` sin explicar, sin `Debug.WriteLine` sueltos (usa el logger).

## 3. Documentación

- `README.md` del repo (créalo): qué es, requisitos (Windows 11, .NET 8, Windows App SDK), modos Mock/Fake/Live y el diálogo
  Conexión (`Ctrl+Shift+F12`), arquitectura (`Core/Networking`, `Auth`, `Persistence`, `Sync`, `Media`, `Bible`), pruebas.
- **Integración con la API**: servidor `https://iris-api.atmosferast.com/api/v1`, o local con `http://<IP>:3020/api/v1`; modo Live y "Probar". Hecho en `README.md`.
- Actualiza `CLAUDE.md` (arquitectura nueva y reglas de la API falsa).

## 4. Reporte

Entrega el reporte de `00-fundamentos/plataforma.md` §6 (incluye en "Pendiente para la integración" todo lo que solo se probó contra la
API falsa) y pregunta al usuario si puede dar el repo por terminado.

## Desviaciones

- La integración ya no es con la API de la Mac sino con el **servidor desplegado** (`https://iris-api.atmosferast.com/api/v1`); el `README.md` del repo documenta ambos casos.
- Las pruebas contra el servidor real (`Tests/LiveApiTests.cs`) son opcionales y se activan con `IRIS_LIVE_URL`, `IRIS_LIVE_EMAIL` e `IRIS_LIVE_PASSWORD`; sin ellas pasan vacías, para no meter credenciales ni tráfico en cada `dotnet test`.
- Quedan en el código restos del modelo con roles (`Role`, `Permission`, `ChurchSummary`, `SwitchChurchAsync`, `auth/switch-church`) que el contrato v1 eliminó (§3). No hacen daño: `Mapping.ToSession` da todos los permisos y ninguna otra iglesia, así que el selector y las restricciones nunca se activan, y `auth/switch-church` no se llama. Es limpieza pendiente, no un fallo.

## Resultado en Windows (2026-10-07)

| Punto | Resultado |
|---|---|
| `dotnet build Iris.csproj -p:Platform=x64` | ✅ 0 errores, 0 advertencias (tras borrar `obj\x64`; con la caché vieja fallaba el XAML con `LeaderOption`) |
| `dotnet build … -c Release` | ✅ 0 errores; 1 advertencia de recorte IL2059 en `XamlTypeInfo.g.cs` (código generado por WinUI) |
| `dotnet test` | ✅ 90 pasan (88 de la API falsa y lógica + 2 contra el servidor real) |
| Servidor real: salud, login, `/auth/me`, `/church` (con `storage.breakdown`), personas, tipos, canciones (189), medios, `download-url`, `/sync` completo, registros, sesiones | ✅ |
| Servidor real: contraseña mala → `INVALID_CREDENTIALS`; sin token → 401; persona con `id` propio repetido → 200; nombre duplicado → `PERSON_NAME_TAKEN`; borrar → 204; refresh; sign-out | ✅ |
| Servidor real: Biblia → 404 `NOT_FOUND` (apagada para todo Iris) | ✅ |
| Segundo `/sync` desde el cursor guardado no aplica nada | ✅ |
| Abrir la app, recorrer pantallas, tres columnas, TV con dos monitores, audio y video reales | ⚠️ no verificado (sesión sin interfaz); se revisa a mano con las listas de las fases 09 y 11 |

Notas del servidor: el login está limitado a 5 por minuto por IP (las pruebas usan un solo login); reusar un refresh token
dentro de 30 s devuelve un `AuthResult` válido por diseño (documentado en el contrato §4.1).
