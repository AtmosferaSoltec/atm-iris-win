# Contrato de la API de Iris · v1

> **Fuente de verdad** de lo que la API expone y de lo que la web, el iPad y Windows consumen.
> El original vive en `atm-iris-api/docs/contract/api-v1.md`. Los otros tres repos tienen una
> **copia idéntica** en `docs/api-contract.md`.
>
> Los cuatro agentes trabajan **a la vez** contra este documento: la API lo implementa y los
> clientes lo consumen sin poder probarlo todavía. Por eso:
>
> - **Nadie cambia el contrato por su cuenta.** Si un agente encuentra un hueco o una
>   contradicción, sigue la interpretación más conservadora y lo anota en la sección
>   *Desviaciones* de su fase. El revisor final unifica.
> - Los nombres de campos, rutas, enums y códigos de error de aquí son **exactos**.
>   Mayúsculas, camelCase y plurales importan.

---

## 1. Convenciones generales

| Tema | Regla |
|---|---|
| Base | `http://<host>:3020/api/v1` en local. Todas las rutas de abajo cuelgan de ahí |
| Formato | JSON UTF-8. `Content-Type: application/json` en peticiones con cuerpo |
| Autenticación | `Authorization: Bearer <accessToken>`. Todo es privado salvo lo marcado como **Pública** |
| Fechas | ISO-8601 en UTC con milisegundos: `"2026-10-05T15:30:31.022Z"` |
| IDs | UUID en texto (`"01a10cb0-1d73-709f-b03e-c6c9c36a17f0"`). El servidor genera UUID v7; los clientes pueden mandar UUID v4 o v7 donde se indique `id?` |
| Enums | Viajan en **minúsculas** en inglés (`"ios"`, `"image"`, `"skipped"`) |
| Textos | Todo `message` está en español y se puede mostrar tal cual |
| Vacíos | Un campo opcional sin valor viaja como `null`, nunca se omite en respuestas |
| Request id | El API devuelve `X-Request-Id` en cada respuesta. Si el cliente manda uno, se respeta. Sirve para cruzar logs |
| IP del visitante | La web reenvía `X-Forwarded-For` (ver plan 01 de la API). Las apps nativas no lo mandan |

### 1.1 Respuestas exitosas

```jsonc
// Un objeto
{ "data": { ... } }
// Una lista sin paginar
{ "data": [ ... ] }
// Una lista paginada
{ "data": [ ... ], "meta": { "page": 1, "limit": 20, "total": 134, "totalPages": 7 } }
// Sin contenido
HTTP 204, sin cuerpo
```

Paginación: query `page` (≥ 1, por defecto 1) y `limit` (1–100, por defecto 20), salvo donde se indique otro máximo.

### 1.2 Errores

```json
{
  "statusCode": 409,
  "code": "PERSON_NAME_TAKEN",
  "message": "Ya existe una persona con ese nombre.",
  "errors": { "name": "Ya existe una persona con ese nombre." },
  "timestamp": "2026-10-05T15:30:31.022Z",
  "path": "/api/v1/people"
}
```

- `code` es estable y es lo que el cliente compara. `message` puede cambiar.
- `errors` solo aparece cuando hay errores por campo. Las claves usan notación de punto para lo anidado: `"blocks.2.name"`, `"client.platform"`.
- Un recurso de otra iglesia, o borrado, responde **404** (nunca 403: un 403 confirmaría que existe).

| Código | HTTP | Cuándo |
|---|---|---|
| `VALIDATION_FAILED` | 400 | El cuerpo o la query no cumplen el esquema. Trae `errors` |
| `UNAUTHORIZED` | 401 | Sin token, token inválido o vencido, sesión cerrada. **El cliente refresca una vez y reintenta** |
| `FORBIDDEN` | 403 | Reservado. La v1 no tiene permisos por rol (§3), así que hoy la API no lo devuelve |
| `NOT_FOUND` | 404 | No existe en esta iglesia |
| `CONFLICT` | 409 | Choque genérico de unicidad |
| `TOO_MANY_REQUESTS` | 429 | Límite de peticiones |
| `INTERNAL_ERROR` | 500 | Cualquier fallo no controlado (mensaje genérico) |
| `INVALID_CREDENTIALS` | 401 | Login con correo o contraseña incorrectos |
| `EMAIL_TAKEN` | 409 | Registro con un correo existente |
| `INVALID_REFRESH_TOKEN` | 401 | El refresh no sirve: vencido, revocado o reusado. **Volver a la pantalla de acceso** |
| `RESET_CODE_INVALID` | 400 | Código de recuperación incorrecto o vencido |
| `RESET_LIMIT_REACHED` | 400 | Código quemado por intentos |
| `INVALID_CURRENT_PASSWORD` | 400 | Cambio de contraseña con la actual incorrecta |
| `PERSON_NAME_TAKEN` | 409 | Persona duplicada (comparación por *nameKey*, §2) |
| `SERVICE_TYPE_NAME_TAKEN` | 409 | Tipo de servicio duplicado |
| `UNSUPPORTED_MEDIA_TYPE` | 415 | Tipo de archivo no permitido para ese `kind` |
| `FILE_TOO_LARGE` | 413 | El archivo supera el máximo del `kind` |
| `STORAGE_QUOTA_EXCEEDED` | 413 | La iglesia superaría su cuota de almacenamiento |
| `UPLOAD_NOT_FOUND` | 400 | Se confirma una subida que no existe, venció o cuyo archivo no llegó al almacenamiento |
| `ID_CONFLICT` | 409 | Un `id` enviado por el cliente ya pertenece a otro recurso |

---

## 2. Reglas compartidas

- **nameKey**: forma con la que se comparan nombres de personas, tipos de servicio y títulos de canciones. Es la cadena con espacios recortados, los espacios internos colapsados a uno, los diacríticos quitados (NFD sin marcas combinantes) y todo en minúsculas. `"  José   Pérez "` → `"jose perez"`. La API lo calcula y guarda; los clientes lo usan para validar antes de enviar.
- **Borrado suave**: personas, tipos de servicio, canciones, medios y registros de tiempos no se borran: se marcan con `deletedAt`. Las listas no los devuelven; la sincronización sí los informa como borrados (§12).
- **Último en escribir gana**: no hay bloqueo optimista en v1. `updatedAt` siempre refleja la última escritura.
- **Idempotencia de creación**: los endpoints de creación marcados con `id?` aceptan un UUID generado por el cliente. Si ese id ya existe **en la misma iglesia**, la API no crea otro: responde 200 con el recurso existente (y lo actualiza en los `PUT`). Si existe en otra iglesia: `409 ID_CONFLICT`. Las consolas lo usan para reintentar desde su cola sin duplicar.
- **Zona horaria**: cada iglesia tiene `timezone` (IANA, por defecto `"America/Lima"`). Todas las fechas viajan en UTC; "hoy", "este mes" y los horarios de los servicios se calculan en la zona de la iglesia.
- **Horario de un servicio**: `weekday` 1 = domingo … 7 = sábado; `hour` 0–23; `minute` 0–59; hora local de la iglesia.

---

## 3. Una cuenta por iglesia

La v1 **no tiene roles, permisos, equipo ni invitaciones**. Cada iglesia tiene una sola cuenta (correo y contraseña) y quien entra con ella puede hacer todo en esa iglesia. La misma cuenta sirve en el iPad, la web y Windows.

- Se crea al registrarse (`POST /auth/sign-up`): crea la iglesia y su cuenta a la vez.
- Una cuenta pertenece a una sola iglesia. No hay selector de iglesia.
- Los clientes **no ocultan ni deshabilitan acciones** según quién entra: todas están disponibles.

---

## 4. Tipos comunes

```ts
type Platform = "web" | "ios" | "windows";

type SessionView = {
  user: { id: string; email: string; fullName: string };
  church: { id: string; name: string; timezone: string };
  session: { id: string; platform: Platform; deviceName: string | null };
};

type AuthResult = SessionView & {
  accessToken: string;
  accessTokenExpiresAt: string;   // ~15 min
  refreshToken: string;
  refreshTokenExpiresAt: string;  // 60 días desde el último uso
};

type ClientInfo = { platform: Platform; deviceName?: string };  // deviceName ≤ 80
```

### 4.1 Ciclo de tokens que implementan todos los clientes

1. Login o registro → guardar `AuthResult` (refresh en almacenamiento seguro).
2. Cada petición lleva el access token. Si al access token le queda menos de 60 s, refrescar **antes** de la petición.
3. Si una petición responde `401 UNAUTHORIZED`: refrescar **una vez** y reintentar. Varias peticiones concurrentes comparten el **mismo** refresh en vuelo (*single flight*).
4. Si `POST /auth/refresh` responde `401` (`INVALID_REFRESH_TOKEN` o `UNAUTHORIZED`): borrar tokens y volver a la pantalla de acceso.
5. Un error de red o 5xx en el refresh **no** cierra la sesión: se reintenta más tarde. Las consolas siguen trabajando sin conexión (§12).

---

## 5. Autenticación — `/auth`

| Método | Ruta | Cuerpo | Respuesta | Acceso |
|---|---|---|---|---|
| POST | `/auth/sign-up` | `{ churchName, fullName, email, password, client: ClientInfo }` | 201 `AuthResult` | Pública |
| POST | `/auth/sign-in` | `{ email, password, client: ClientInfo }` | 200 `AuthResult` | Pública |
| POST | `/auth/refresh` | `{ refreshToken }` | 200 `AuthResult` | Pública |
| POST | `/auth/sign-out` | — | 204 | Sesión |
| POST | `/auth/sign-out-all` | — | 204 (cierra también la actual) | Sesión |
| GET | `/auth/me` | — | 200 `SessionView` | Sesión |
| PATCH | `/auth/me` | `{ fullName }` | 200 `SessionView` | Sesión |
| POST | `/auth/change-password` | `{ currentPassword, password, passwordConfirmation }` | 204. Cierra **las demás** sesiones | Sesión |
| GET | `/auth/sessions` | — | 200 `DeviceSession[]` | Sesión |
| DELETE | `/auth/sessions/:id` | — | 204 (solo sesiones propias; la actual equivale a sign-out) | Sesión |
| POST | `/auth/forgot-password` | `{ email }` | 200 `{ message }`, siempre igual | Pública |
| POST | `/auth/verify-reset-code` | `{ email, code }` | 200 `{ valid: true }` | Pública |
| POST | `/auth/reset-password` | `{ email, code, password, passwordConfirmation }` | 200 `{ message }`. Cierra **todas** las sesiones | Pública |

```ts
type DeviceSession = {
  id: string; platform: Platform; deviceName: string | null;
  createdAt: string; lastUsedAt: string; ipAddress: string | null;
  isCurrent: boolean;
};
```

Reglas:
- Correo: se recorta y se pasa a minúsculas. Contraseña: 8–128 caracteres. Código: exactamente 6 dígitos.
- `sign-in` entra a la iglesia de la cuenta.
- Recuperación: el código vence a los **15 min**, admite **5 intentos** y se envían como máximo **3 por día** por cuenta.
- Límites por IP: `sign-in` 5/min · `sign-up` 5/h · `forgot-password` 3/h · `verify-reset-code` y `reset-password` 10/15 min · general 300/min.

Ejemplo `AuthResult`:

```json
{
  "data": {
    "user": { "id": "01a1…", "email": "pastor@vidanueva.org", "fullName": "Daniel Ruiz" },
    "church": { "id": "01a2…", "name": "Iglesia Vida Nueva", "timezone": "America/Lima" },
    "session": { "id": "01a3…", "platform": "ios", "deviceName": "iPad de la sala" },
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9…",
    "accessTokenExpiresAt": "2026-10-05T15:45:31.022Z",
    "refreshToken": "01a3….0.tcH1uiV1FWRgmkBfseKFwYHjxV6UDU7qJuShu73pRIA",
    "refreshTokenExpiresAt": "2026-12-04T15:30:31.022Z"
  }
}
```

---

## 6. Iglesia — `/church`

| Método | Ruta | Cuerpo | Respuesta | Acceso |
|---|---|---|---|---|
| GET | `/church` | — | `Church` | Sesión |
| PATCH | `/church` | `{ name?, timezone? }` | `Church` | Sesión |
| PUT | `/church/modules` | `ChurchModules` | `Church` | Sesión |
| PUT | `/church/projection` | `ProjectionSettingsInput` | `Church` | Sesión |

```ts
type ChurchModules = { bible: boolean; multimedia: boolean; timeControl: boolean };  // Letras siempre activo
type ProjectionFont =
  | "system" | "systemRounded" | "serif" | "georgia" | "avenirNext"
  | "futura" | "gillSans" | "optima" | "baskerville" | "palatino";
type ProjectionSettings = {
  fontFamily: ProjectionFont;
  fontSizePt: number;              // 40–200, referido a una pantalla de 1920 de ancho
  defaultBackgroundId: string | null;  // fondo cuando no hay ninguno elegido; null = negro
};
type ProjectionSettingsInput = ProjectionSettings;  // defaultBackgroundId: "" o null limpian
type Church = {
  id: string; name: string; timezone: string;
  modules: ChurchModules;           // lo que la iglesia ve encendido (su elección y lo disponible en Iris)
  availableModules: ChurchModules;  // módulos que existen hoy en Iris; lo que está en false no se ofrece
  projection: ProjectionSettings;   // cómo se ve la letra proyectada; igual en todas las consolas
  storage: StorageUsage;
  createdAt: string; updatedAt: string;
};
type StorageUsage = {
  usedBytes: number;                // suma de breakdown
  quotaBytes: number;
  breakdown: {
    musicBytes: number;             // audios (sección Música)
    backgroundBytes: number;        // imágenes y videos con isBackground (sección Fondos)
    mediaBytes: number;             // el resto de imágenes y videos (sección Multimedia)
  };
};
```

- `name` 1–120. `timezone` debe ser una zona IANA válida.
- Iglesia nueva: los tres módulos en `true`, cuota de **5 GiB** (`5368709120`).
- **Almacenamiento** (`storage`): la cuota es una sola por iglesia y la comparten música, fondos y multimedia.
  `breakdown` dice cuánto ocupa cada sección (solo medios no borrados; las subidas en curso no cuentan aquí).
  En `/sync` la iglesia solo viaja cuando cambia, así que una consola que muestre el uso lo pide a `GET /church`.
- **Letra proyectada** (`projection`): tipografía, tamaño y fondo por defecto, iguales en todas las consolas de la
  iglesia. `fontFamily` es una clave, no un nombre de fuente: cada cliente la traduce a la fuente real de su
  plataforma (10 claves — ver `src/modules/church/projection-fonts.ts` en la API para la traducción sugerida por
  plataforma). `defaultBackgroundId` es el id de un fondo (uno de los degradados fijos o una imagen de la
  biblioteca) que se muestra cuando la consola no tiene ninguno elegido; un id que ya no existe (una imagen
  borrada) cae a negro sin avisar, los clientes no lo validan contra nada.
- **Módulos del sistema** (`availableModules`): el dueño de Iris puede apagar un módulo para todas las iglesias
  (tabla `system_features`, sin pantalla). Mientras esté apagado, `modules` lo trae en `false` aunque la iglesia lo
  haya encendido (su elección se guarda y vuelve sola al habilitarlo), y los clientes **no lo muestran**, ni
  siquiera el interruptor en ajustes. Al cambiar un interruptor del sistema, todas las iglesias suben de versión
  en `/sync` y las consolas lo reciben en el siguiente ciclo. **Hoy la Biblia está apagada** (`bible: false`).

---

## 7. Reservado

Antes era el equipo (`/members`, `/invitations`). La v1 no tiene equipo, roles ni invitaciones (§3). El número se deja libre para no mover las referencias del resto del documento.

---

## 8. Personas — `/people`

| Método | Ruta | Cuerpo | Respuesta | Acceso |
|---|---|---|---|---|
| GET | `/people` | — | `Person[]` (por nombre, sin borrados) | Sesión |
| POST | `/people` | `{ id?, name }` | 201 `Person` (200 si el `id` ya existía) | Sesión |
| PATCH | `/people/:id` | `{ name }` | `Person` | Sesión |
| DELETE | `/people/:id` | — | 204 | Sesión |

```ts
type Person = {
  id: string; name: string;
  blockCount: number;             // bloques dirigidos en registros no borrados, sin contar los omitidos
  createdAt: string; updatedAt: string;
};
```

- `name` 1–80. Único por *nameKey* entre las personas no borradas → `PERSON_NAME_TAKEN`.
- Borrar una persona no cambia las plantillas (no llevan responsable). Los registros conservan `personId` y `personName`.

---

## 9. Tipos de servicio — `/service-types`

| Método | Ruta | Cuerpo | Respuesta | Acceso |
|---|---|---|---|---|
| GET | `/service-types` | — | `ServiceType[]` (por nombre) | Sesión |
| GET | `/service-types/:id` | — | `ServiceType` | Sesión |
| POST | `/service-types` | `ServiceTypeInput & { id? }` | 201 `ServiceType` | Sesión |
| PUT | `/service-types/:id` | `ServiceTypeInput` | `ServiceType` (reemplazo completo; crea si no existe) | Sesión |
| DELETE | `/service-types/:id` | — | 204 | Sesión |

```ts
type Schedule = { weekday: number; hour: number; minute: number };
type ServiceTypeInput = {
  name: string;                   // 1–60, único por nameKey → SERVICE_TYPE_NAME_TAKEN
  color: "#FFB547" | "#FF7A59" | "#F0508C" | "#9B5CFF" | "#4E5BFF" | "#3DDC97";
  schedule: Schedule | null;
  blocks: BlockTemplateInput[];   // 0–30, el orden del arreglo es el orden del servicio
};
type BlockTemplateInput = {
  id?: string; name: string;      // 1–60
  plannedMinutes: number;         // entero 1–240 (sin responsable: se registra en cada servicio)
};
type BlockTemplate = { id: string; name: string; plannedMinutes: number };
type ServiceType = {
  id: string; name: string; color: string; schedule: Schedule | null;
  blocks: BlockTemplate[];        // ya ordenados
  createdAt: string; updatedAt: string;
};
```

- Un tipo "controla tiempo" si tiene al menos un bloque. Sin bloques es "Solo proyección".
- Los bloques sin `id` reciben uno nuevo; los que traen `id` lo conservan. Los que no vienen en el arreglo se eliminan.
- Con el módulo `timeControl` apagado, la API **acepta y guarda** bloques igual; son los clientes los que no los muestran ni los editan (para no borrarlos por accidente, los clientes reenvían los bloques existentes intactos).

---

## 10. Canciones — `/songs`

| Método | Ruta | Cuerpo / query | Respuesta | Acceso |
|---|---|---|---|---|
| GET | `/songs` | `?search=&page=&limit=&sort=title\|-updatedAt` | paginado `SongSummary[]` | Sesión |
| GET | `/songs/:id` | — | `Song` | Sesión |
| POST | `/songs` | `SongInput & { id? }` | 201 `Song` | Sesión |
| PUT | `/songs/:id` | `SongInput` | `Song` (crea si no existe) | Sesión |
| DELETE | `/songs/:id` | — | 204 | Sesión |

```ts
type SongSectionInput = { label: string | null; text: string };   // label ≤ 40 (opcional; la web lo escribe como `#Coro` en la letra), text 1–2000
type SongInput = {
  title: string;                  // 1–120
  author: string;                 // 0–120 ("" si no hay)
  sections: SongSectionInput[];   // 1–80, en orden
};
type SongSection = { id: string; label: string | null; text: string };
type Song = {
  id: string; title: string; author: string;
  sections: SongSection[];
  createdAt: string; updatedAt: string;
};
type SongSummary = {
  id: string; title: string; author: string;
  sectionCount: number; firstLine: string | null;
  updatedAt: string;
};
```

- `search` busca sin acentos ni mayúsculas en título, autor y texto de las secciones; ordena por relevancia cuando hay búsqueda. `limit` máximo 100. Sin `sort`, se ordena por título.
- Los títulos **no** son únicos (dos iglesias pueden cantar dos versiones). Dos canciones con el mismo título son dos letras distintas.
- El texto llega ya dividido en secciones: el formato de texto plano (`[Coro]`, línea en blanco entre diapositivas) lo resuelve cada cliente. Regla de referencia: `atm-iris-web/src/lib/lyrics.ts`.

---

## 11. Multimedia — `/media`

La subida va **directo al almacenamiento** (S3 compatible: Cloudflare R2 en producción, MinIO en local) con una URL firmada. La API nunca recibe los bytes.

| Método | Ruta | Cuerpo / query | Respuesta | Acceso |
|---|---|---|---|---|
| POST | `/media/uploads` | `{ kind, fileName, contentType, sizeBytes }` | 201 `UploadTicket` | Sesión |
| POST | `/media` | `{ uploadId, title, description?, durationSeconds?, width?, height?, isBackground? }` | 201 `MediaAsset` | Sesión |
| GET | `/media` | `?kind=&search=&isBackground=&page=&limit=` (`kind` acepta varios separados por coma: `image,video`) | paginado `MediaAsset[]` (más reciente primero) | Sesión |
| GET | `/media/:id` | — | `MediaAsset` | Sesión |
| GET | `/media/:id/download-url` | — | `{ url, expiresAt }` (GET firmado, 1 h) | Sesión |
| PATCH | `/media/:id` | `{ title?, description?, isBackground? }` | `MediaAsset` | Sesión |
| DELETE | `/media/:id` | — | 204 (libera la cuota; el archivo se borra del almacenamiento después) | Sesión |

```ts
type MediaKind = "image" | "video" | "audio";
type UploadTicket = {
  uploadId: string;
  uploadUrl: string;              // PUT firmado
  headers: Record<string, string>;// el cliente los manda tal cual en el PUT (incluye Content-Type)
  expiresAt: string;              // 1 h
};
type MediaAsset = {
  id: string; kind: MediaKind; title: string; description: string | null;
  fileName: string; contentType: string; sizeBytes: number;
  durationSeconds: number | null; // audio y video
  width: number | null; height: number | null;   // imagen y video
  isBackground: boolean;          // fondo de las letras (imagen o video); ver "Fondos" abajo
  createdAt: string; updatedAt: string;
};
```

| `kind` | Tipos permitidos | Máximo |
|---|---|---|
| `image` | `image/jpeg`, `image/png`, `image/webp` | 20 MB |
| `video` | `video/mp4`, `video/quicktime` | 2 GB |
| `audio` | `audio/mpeg`, `audio/mp4`, `audio/aac`, `audio/wav`, `audio/x-wav` | 200 MB |

**Fondos** (`isBackground: true`): imágenes o videos que las consolas muestran detrás de la letra. Al confirmar (`POST /media`) o al marcarlo (`PATCH`) la API valida, con los metadatos del medio, y responde 400 `VALIDATION_FAILED` (`errors.isBackground`) con el motivo:

| | Imagen | Video |
|---|---|---|
| Formato | JPG, PNG, WebP | solo MP4 |
| Proporción | 16:9 (±2 %) | 16:9 (±2 %) |
| Tamaño | 1280 × 720 a 3840 × 2160; recomendado 1920 × 1080 | 1280 × 720 a 1920 × 1080 |
| Duración | — | hasta **30 s**; las consolas lo repiten en bucle y sin sonido |
| Peso | hasta 10 MB | hasta 100 MB |

Un audio no puede ser fondo. Los fondos cuentan para la cuota como cualquier medio.

Flujo:
1. `POST /media/uploads` valida tipo, tamaño y cuota (`UNSUPPORTED_MEDIA_TYPE`, `FILE_TOO_LARGE`, `STORAGE_QUOTA_EXCEEDED`).
2. El cliente hace `PUT uploadUrl` con los `headers` y los bytes.
3. `POST /media` confirma: la API comprueba que el objeto existe y que su tamaño coincide (`UPLOAD_NOT_FOUND` si no) y crea el `MediaAsset`. Los metadatos (`durationSeconds`, `width`, `height`) los mide el cliente que sube.
4. Las consolas descargan con `GET /media/:id/download-url` y guardan el archivo en caché por `id` + `updatedAt`.

**Secciones de la web**: **Música** (`kind=audio`, el repertorio de pistas que suenan en el salón), **Fondos**
(`isBackground=true`) y **Multimedia** (`kind=image,video&isBackground=false`, material para una ocasión). Las tres
son el mismo `MediaAsset` y comparten la cuota. Las letras (§10) son otra cosa y no ocupan almacenamiento.

**Qué descarga una consola y cuándo** (los archivos no viven en una carpeta del equipo: salen de aquí):
- **Imágenes y fondos** (también los videos de fondo): todos, apenas llegan por `/sync`. Son livianos y se
  necesitan al instante.
- **Música y videos**: solo los que se usan. La descarga empieza al agregarlos al servicio en la consola (o al
  reproducirlos, si todavía no estaban), y el archivo se queda en el equipo para la próxima vez.
- Lo borrado (`deleted.media` en `/sync`, o un `updatedAt` nuevo) se elimina del equipo en la siguiente
  sincronización. El archivo sigue en el almacenamiento 24 h después del borrado, así una consola atrasada no se
  queda a medias.

Las subidas sin confirmar vencen a la hora y se limpian solas.

---

## 12. Sincronización de las consolas — `/sync`

Las consolas (iPad y Windows) trabajan **sin conexión**: guardan una copia local de la iglesia y la ponen al día con este feed. La web no lo usa.

| Método | Ruta | Query | Respuesta |
|---|---|---|---|
| GET | `/sync/changes` | `since` (cursor, `"0"` la primera vez) · `limit` (1–500, por defecto 200) | `SyncPage` |

```ts
type SyncPage = {
  church: Church | null;          // presente si la iglesia (nombre, zona, módulos) cambió después de `since`
  changes: {
    people: Person[];
    serviceTypes: ServiceType[];
    songs: Song[];                // completas, con secciones
    media: MediaAsset[];
    serviceRecords: ServiceRecord[];
  };
  deleted: {                      // ids borrados (suave) después de `since`
    people: string[]; serviceTypes: string[]; songs: string[]; media: string[]; serviceRecords: string[];
  };
  cursor: string;                 // guardar y mandar como `since` la próxima vez
  hasMore: boolean;               // si es true, pedir otra página de inmediato con el cursor nuevo
};
```

Reglas:
- Cada fila sincronizable lleva un número de versión global y creciente. El cursor es la versión más alta entregada; el cliente lo trata como **texto opaco**.
- `limit` cuenta entidades (cambios + borrados) en total. Dentro de una página, todo viene ordenado por versión.
- Un recurso que cambió y luego se borró aparece **solo** en `deleted`.
- Primera sincronización: `since=0` y repetir mientras `hasMore` sea `true`. Eso es la copia completa.
- Cuándo sincronizar (clientes): al abrir la app con sesión, al volver al Inicio, cada 5 min mientras el Inicio está visible, al recuperar la conexión, y con el botón "Actualizar". **Nunca** durante un servicio en curso en la consola: la consola trabaja con la copia que tenía al iniciarlo.
- Escrituras sin conexión: las consolas encolan (*outbox*) las creaciones con `id` propio (`POST /people`, `PUT /service-records/:id`, `PUT /service-types/:id`) y las reenvían en orden al volver la conexión. Gracias a la idempotencia (§2) un reintento nunca duplica.

---

## 13. Biblia — `/bible`

Texto: **Reina-Valera 1909** (dominio público), código `rvr1909`.

| Método | Ruta | Respuesta |
|---|---|---|
| GET | `/bible/translations` | `BibleTranslation[]` |
| GET | `/bible/translations/:code/books` | `BibleBook[]` (canónico, Génesis → Apocalipsis) |
| GET | `/bible/translations/:code/books/:bookId/chapters/:chapter` | `{ bookId, chapter, verses: { number: number; text: string }[] }` |
| GET | `/bible/translations/:code/download` | `BibleDownload` (gzip; `ETag` = versión) |

```ts
type BibleTranslation = { code: string; name: string; language: "es"; version: number; sizeBytes: number };
type BibleBook = {
  id: string;                     // código USFM: "GEN", "PSA", "JHN", "REV"
  name: string;                   // "Génesis", "Salmos", "Juan", "Apocalipsis"
  testament: "old" | "new";
  chapterCount: number;
  position: number;               // 1–66
};
type BibleDownload = {
  code: string; name: string; version: number;
  books: (BibleBook & { chapters: string[][] })[];   // chapters[c][v] = texto del versículo c+1:v+1
};
```

- Las consolas descargan `download` una vez (y de nuevo si cambia `version`) y buscan sin conexión.
- Requieren sesión; no dependen del módulo `bible` de cada iglesia (el módulo solo oculta la función en la consola).
- Con la Biblia apagada para todo Iris (`availableModules.bible = false`) todas estas rutas responden
  404 `NOT_FOUND` «La Biblia no está disponible por ahora.».

---

## 14. Tiempos — `/service-records`

| Método | Ruta | Cuerpo / query | Respuesta | Acceso |
|---|---|---|---|---|
| GET | `/service-records` | `?from=&to=&serviceTypeId=&page=&limit=` (limit ≤ 500) | paginado `ServiceRecord[]` (más reciente primero) | Sesión |
| GET | `/service-records/:id` | — | `ServiceRecord` | Sesión |
| PUT | `/service-records/:id` | `ServiceRecordInput` | 201 si lo crea · 200 si ya existía | Sesión |
| PATCH | `/service-records/:id/blocks/:blockId` | `{ actualSeconds?, personId? }` | `ServiceRecord` | Sesión |
| DELETE | `/service-records/:id` | — | 204 | Sesión |

```ts
type BlockStatus = "completed" | "skipped" | "adjusted";
type BlockRecordInput = {
  id: string;                     // generado por la consola
  name: string;                   // 1–60
  plannedSeconds: number;         // entero ≥ 0
  actualSeconds: number;          // entero ≥ 0 (0 si skipped)
  personId: string | null;
  personName: string | null;      // nombre en ese momento
  status: "completed" | "skipped";
};
type ServiceRecordInput = {
  date: string;                   // inicio del primer bloque, UTC
  serviceTypeId: string;
  serviceTypeName: string;        // copia, para mostrar si el tipo se borra
  blocks: BlockRecordInput[];     // 1–60, en orden
};
type BlockRecord = BlockRecordInput & { status: BlockStatus };
type ServiceRecord = ServiceRecordInput & {
  id: string; blocks: BlockRecord[];
  createdAt: string; updatedAt: string;
};
```

- `from` y `to` son instantes UTC (inclusivo / exclusivo) sobre `date`.
- `PATCH … { actualSeconds }` marca el bloque `adjusted`. `{ personId }` actualiza también `personName` con el nombre actual (o `null`).
- **Solo tiempos**, nunca contenido proyectado. Las estadísticas (promedios, excesos, por persona, por bloque) las calcula cada cliente con las reglas de `IRIS_SPEC.md` §7.10: exceso = real − previsto si > 0, sin margen; los omitidos no cuentan.

---

## 15. Salud

| Método | Ruta | Respuesta |
|---|---|---|
| GET | `/health` | **Pública**. `{ data: { status: "ok", info: { database: { status: "up" }, storage: { status: "up" } } } }` |
