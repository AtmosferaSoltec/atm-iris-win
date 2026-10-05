# 00 · Fundamentos

## Objetivo

Las piezas que usan todas las fases: modos de datos, configuración de conexión, cliente HTTP, modelos
de transporte con JSON generado, almacenamiento seguro y una **API falsa** dentro de la app que cumple
el contrato. Además, borrar la estructura vieja que ya no se usa.

## Pasos

### 1. Limpieza
- Borra `Services/`, `ViewModels/` y `Views/` (andamiaje anterior a la spec; `CLAUDE.md` lo marca como sin uso). Comprueba que
  nada los referencia y que compila.
- `CLAUDE.md` ya apunta a este plan y su regla sobre datos ya habla de los modos Mock/Fake/Live; quita de la sección
  *Things to know* la línea sobre el andamiaje viejo cuando lo borres.

### 2. Modos de datos y configuración
- `enum DataMode { Mock, Fake, Live }`:
  - **Mock**: los `Mock*` actuales (para trabajar diseño). No pasa por red.
  - **Fake**: los servicios `Live*` reales, pero el `HttpClient` usa el `FakeIrisApiHandler` (abajo). **Es el modo por defecto
    durante el desarrollo.**
  - **Live**: los servicios `Live*` contra la API real (`ApiBaseUrl`).
- `AppSettings` en `ApplicationData.Current.LocalSettings`: `DataMode` (por defecto `Fake` en Debug, `Live` en Release) y
  `ApiBaseUrl` (por defecto `http://localhost:3020/api/v1`).
- Diálogo **"Conexión"** (desarrollo e integración): se abre con `Ctrl+Shift+F12` en la pantalla de acceso. Campos: modo
  (`IrisSegmentedControl` Mock / Fake / Live), URL de la API, botón "Probar" (`GET /health` → "Conectado" / el error), "Guardar y
  reiniciar" (reinicia la composición, no el proceso, si es simple; si no, pide reiniciar la app).
- `AppDependencies`: `Mock()` (existe), `Live(DataMode mode, AppSettings settings)` que registra los `Live*` y el `HttpClient`
  con el transporte adecuado. `App.xaml.cs` elige según la configuración.

### 3. Modelos de transporte (`Core/Networking/Dto/`)
- Un `record` por tipo del contrato con propiedades en PascalCase y `[JsonPropertyName]` donde el nombre difiera
  (o `JsonNamingPolicy.CamelCase` en el contexto). Enums como `string` con un convertidor que tolera valores desconocidos
  (`Unknown`).
- `IrisJsonContext : JsonSerializerContext` con **todos** los DTO (`[JsonSerializable(typeof(...))]`), `DateTimeOffset` ISO con
  milisegundos, `DefaultIgnoreCondition = Never` (el contrato manda `null` explícitos).
- `Core/Networking/Dto/Mapping.cs`: DTO ⇄ modelos de `Core/Models`.

### 4. Cliente HTTP (`Core/Networking/ApiClient.cs`)
- `ApiClient` sobre un `HttpClient` inyectado: `SendAsync<T>(ApiRequest)` que desempaqueta `{ data }`, `SendPageAsync<T>` para
  paginadas, `SendAsync` sin contenido para 204. Todo con `IrisJsonContext`.
- `ApiException` con `StatusCode`, `Code`, `Message` (español), `Errors`; `NetworkException` para fallos de conexión y tiempos
  de espera (mensaje "No pudimos conectarnos. Verifica tu conexión a internet."). 5xx → mensaje genérico.
- `X-Request-Id` en cada petición; registro con `ILogger` (Microsoft.Extensions.Logging con salida a Debug y a un archivo
  rotativo en `LocalFolder/logs`) sin tokens ni contraseñas.
- El token y el refresh los aporta un `DelegatingHandler` (`AuthHandler`, fase 01).

### 5. Almacenamiento seguro (`Core/Auth/TokenStore.cs`)
- `ITokenStore` con `Load`, `Save`, `Clear`. `PasswordVaultTokenStore` usa `Windows.Security.Credentials.PasswordVault`
  (recurso `Iris`, usuario = id del usuario) para el refresh token y su vencimiento. `InMemoryTokenStore` para pruebas.

### 6. API falsa (`Core/Networking/Fake/`)
`FakeIrisApiHandler : HttpMessageHandler` que responde **como el contrato** (mismas rutas, cuerpos, `{ data }`, errores con
`code`/`message`/`errors`, estados HTTP) con un estado en memoria persistido en `LocalFolder/fake-api.json` (para que reiniciar
la app no lo borre):
- Semilla desde `MockChurchData`/`SampleData`: iglesia "Iglesia Vida Nueva" (`America/Lima`), segunda iglesia "Iglesia Monte Sion",
  usuarios `pastor@vidanueva.org` (owner), `operador@vidanueva.org` (operator), contraseña `vidanueva123`; personas, tipos de
  servicio, canciones y registros de la maqueta.
- Tokens: access token falso que vence en **2 minutos** (para ejercitar el refresh), refresh con generación y la regla de gracia y
  reuso del contrato.
- `error@…` → `INVALID_CREDENTIALS`; `existe@…` en registro → `EMAIL_TAKEN`; código de recuperación siempre `123456`.
- Versión de sincronización global creciente en cada escritura, borrado suave, `/sync/changes` paginado.
- Interruptor "Simular sin conexión" en el diálogo Conexión: el handler lanza `HttpRequestException` como si no hubiera red.
- En esta fase implementa **auth** (§5) y `/health`. Cada fase siguiente agrega sus endpoints.

### 7. Modelos de la app
- `UserSession` pasa a: `UserId`, `Email`, `FullName`, `Church` (`Id`, `Name`, `TimeZone` como `TimeZoneInfo` desde IANA con
  `TimeZoneInfo.FindSystemTimeZoneById`, que en .NET 8 acepta IANA con ICU), `Role`, `Permissions` (`IReadOnlySet<Permission>`),
  `Churches`, `SessionId`. Elimina `ChurchName`/`LeaderName` y ajusta todos los usos.
- `enum Permission` con el catálogo del contrato §3 y `session.Can(Permission.SongsManage)`.
- `ChurchClock`: "hoy", horarios y periodos en la zona de la iglesia (reemplaza `DateTime.Now` y `TimeZoneInfo.Local` donde
  corresponda).
- `NameKey` (`Core/Formatting`): alinéalo con el contrato §2 (colapsar espacios internos).

## Criterios de aceptación

- `dotnet build Iris.csproj -p:Platform=x64` limpio; la app abre en modo Mock como antes.
- En modo Fake, `GET /health` responde desde el diálogo Conexión.

## Desviaciones

_(Completar al cerrar la fase.)_
