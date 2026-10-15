# 12 · Distribución y actualizaciones (instalador `.exe` + actualización obligatoria)

## Objetivo

Que un cliente entre a **https://iris.atmosferast.com/downloads**, descargue **`Iris-Setup.exe`**, haga doble clic y use la
app. Sin Microsoft Store, sin clonar el repo, sin PowerShell. Cuando haya una versión nueva, la app lo detecta al abrir,
**obliga a actualizar** y se actualiza sola, sin desinstalar nada. Lo mismo para el iPad (aviso que lleva a TestFlight).

## Decisiones ya tomadas (no re-discutir)

| Tema | Decisión | Por qué |
|---|---|---|
| Canal | Instalador propio, **no** Microsoft Store | El dueño no quiere la Store. |
| Herramienta | **Velopack** (`vpk`, NuGet `Velopack` 1.2.161) | `Setup.exe` de un solo archivo, instala por usuario sin administrador, actualiza con parches y reinicia. |
| Dónde se aloja | **`https://iris.atmosferast.com/downloads/windows/`**: carpeta estática en el VPS, servida por nginx | El certificado de Cloudflare es `*.atmosferast.com`: **no cubre** `downloads.iris.atmosferast.com` (un comodín cubre un solo nivel). En el mismo host no hace falta otro certificado. |
| Landing | Ruta pública **`/downloads`** de `atm-iris-web` (sin sesión) | Un solo sitio; ya tiene los tokens de diseño. |
| Quién decide que hay que actualizar | **La base de datos**: tabla `app_versions` (una fila por plataforma), cambiada a mano. `GET /app-version` (contrato §17) | El dueño controla cuándo se exige, sin tocar código. |
| Quién descarga e instala | **Velopack**, leyendo `releases.win.json` de la carpeta | Es lo que sabe reemplazar la app en caliente. |
| Actualización | Siempre obligatoria cuando `latestVersion` > versión instalada | Una sola versión en el campo; menos soporte. |
| Despliegue | Empujar a la rama **`production`** (GitHub Actions, runner Windows). La versión sale del archivo **`VERSION`** | Mismo flujo que la API y la web. |
| Firma | Sin firmar en el piloto | Costo anual; ver §8. |

## Reglas que no se negocian

- **La app nunca queda bloqueada por una actualización que no puede revisar.** Sin internet, API caída, error de cualquier tipo
  o feed sin la versión todavía → la app abre normal. El tiempo máximo de la consulta es 8 s (`UpdateCheck`).
- **Nunca en medio de un servicio.** El diálogo no se muestra con la consola en vivo (`AppRoute.Console`); aparece al volver a Inicio.
- **Copia de desarrollo** (no instalada por Velopack, `IsInstalled == false`): no consulta nada.
- **Solo se exige lo que ya se puede descargar.** Orden obligatorio al publicar: (1) workflow termina y el instalador está en
  el servidor, (2) *después* se sube la versión en la base. Al revés, se bloquea a las iglesias sin salida.

## Cómo sube una versión (lo que hace el dueño)

1. En `atm-iris-win`, editar el archivo **`VERSION`** (raíz del repo): `1.0.0` → `1.0.1`. Es la única fuente del número.
2. Empujar a `production` (`git push origin develop:production`, o merge). El workflow
   `.github/workflows/release-windows.yml` prueba, empaqueta y sube al VPS. Falla si esa versión ya existe.
3. Cuando termine en verde, exigirla a los clientes (en `atm-iris-api`, con `DATABASE_URL` de producción):
   `pnpm db:set-app-version windows 1.0.1`  — o a mano: `UPDATE iris.app_versions SET version='1.0.1', updated_at=now() WHERE platform='windows';`
4. Los clientes con la 1.0.0 ven el diálogo al abrir → **Actualizar** → descarga → se reinicia en la 1.0.1.

iPad: igual, pero el paquete va por TestFlight/App Store; cuando esté disponible:
`pnpm db:set-app-version ios 1.0.1 https://testflight.apple.com/join/XXXX "Mejoras en la consola"`.

## Arquitectura

```
git push production ─▶ GitHub Actions (windows-latest)
                         │ dotnet test · dotnet publish -p:IrisInstaller=true (sin empaquetar, autocontenida)
                         │ vpk download (versión anterior, para el parche) · vpk pack
                         ▼ scp (SSH_HOST / SSH_USER / SSH_KEY)
VPS: /var/www/iris-downloads/windows/ ── nginx ──▶ https://iris.atmosferast.com/downloads/windows/
        Iris-Setup.exe · releases.win.json · *.nupkg
                                        ▲                          ▲
        /downloads (Next.js) enlaza ────┘                          └── la app descarga el parche

App al abrir:  GET /app-version?platform=windows&version=1.0.0   (API, público)
   updateRequired=true  ──▶  ¿el feed tiene la versión?  ──▶ sí: diálogo que bloquea → Velopack descarga y reinicia
                                                           └─▶ no: abre normal
```

## Estado (qué quedó hecho en la Mac)

| Pieza | Dónde | Verificado |
|---|---|---|
| Tabla `app_versions`, endpoint público, script `db:set-app-version` | `atm-iris-api` | Pruebas unitarias y e2e en verde |
| Contrato §17 | `docs/api-contract.md` (las 4 copias) | — |
| Página `/downloads` pública | `atm-iris-web` | Playwright en verde |
| Aviso bloqueante del iPad | `atm-iris-ios` (`UpdateRequiredView`, `AppUpdateGate`) | Build y pruebas en verde |
| Lógica (`UpdateCheck`, `AppVersion`), cliente de `/app-version`, API falsa, `UpdateGateViewModel` + `UpdateGateSheet`, `VelopackInstaller`, `Program.cs` | `atm-iris-win` | Pruebas de Core (112), compila todo el C# y `x:Bind` OK con `Scripts/MacCheck` |
| Workflow, `Release-Windows.ps1`, `vps-setup-downloads.sh`, `VERSION` | `atm-iris-win` | **Sin ejecutar** |

## Lo que falta (necesita Windows o el VPS)

> La pasada de pruebas, paso a paso y con tabla de resultados, está en [`PRUEBAS-WINDOWS.md`](PRUEBAS-WINDOWS.md).

**[Win]** — probar de verdad (nada de esto se puede correr en la Mac):

1. `dotnet build Iris.csproj -p:Platform=x64` sin errores (el XAML nuevo: `UpdateGateSheet`, y los cambios de bienvenida/login).
2. **Recorte (`PublishTrimmed`)** con `-p:IrisInstaller=true`: `dotnet publish` y abrir el resultado. Si Velopack o algo se rompe
   por el recorte, excluir ese ensamblado (`<TrimmerRootAssembly Include="Velopack" />`).
3. `Program.cs`: el `Main` propio solo existe con `DISABLE_XAML_GENERATED_MAIN` (definida en `Iris.csproj`). Comprobar que
   F5 y el perfil MSIX siguen abriendo la app.
4. `Scripts\Release-Windows.ps1` genera `Releases\Iris-Setup.exe`. Instalarlo en una **PC limpia** (sin .NET ni Windows App SDK).
5. Datos: instalar, iniciar sesión, cerrar, abrir (sesión conservada). Desinstalar desde Aplicaciones instaladas: la app se va
   y `%LOCALAPPDATA%\Iris` (datos) permanece. `packId = IrisConsole` evita que Velopack, que borra `%LOCALAPPDATA%\IrisConsole`
   al desinstalar, se lleve los datos que `AppPaths` guarda en `%LOCALAPPDATA%\Iris`.
6. Actualización: instalar la 1.0.0, publicar la 1.0.1 (carpeta local servida por HTTP o el VPS), poner `app_versions.windows = 1.0.1`
   y abrir la 1.0.0: aparece el diálogo, no se cierra con Esc ni haciendo clic fuera, **Actualizar** descarga y reinicia en la
   1.0.1 con la sesión intacta. Sin internet: abre normal. Con un servicio en curso: no aparece.
7. `Package.appxmanifest` (`Version="1.0.0.0"`) queda solo para el perfil MSIX de desarrollo; el número real es `VERSION`.

**[VPS]** — una vez (ver `Scripts/vps-setup-downloads.sh`, idempotente, revisa `nginx -t` antes de recargar):

```sh
ssh atmosfera 'bash -s' < Scripts/vps-setup-downloads.sh
```

**[GitHub]** — secretos del repo `atm-iris-win`: `SSH_HOST`, `SSH_USER`, `SSH_KEY` (los mismos que `atm-iris-web` y `atm-iris-api`;
si son secretos de la organización ya están). Crear la rama `production` en este repo.

**[API]** — desplegar `atm-iris-api` a producción para que exista la tabla (`migrate` corre en el despliegue) antes de publicar
la primera versión. Mientras la tabla no exista o responda error, las apps abren normal.

## Datos y desinstalación

- Datos locales (sesión, copia SQLite, caché, registros): `%LOCALAPPDATA%\Iris` (`AppPaths`).
- Las instalaciones MSIX de desarrollo guardaban en el `LocalState` del paquete; **no se migran** (no hay clientes en MSIX).

## 8. SmartScreen (sin firma)

Un `Setup.exe` sin firmar muestra "Windows protegió tu PC". El cliente pulsa **Más información → Ejecutar de todas formas**;
la landing `/downloads` ya lo explica. Se resuelve con un certificado de firma de código (OV/EV, o Azure Trusted Signing según
disponibilidad en el país): solo se añade `--signParams` a `vpk pack`. Con EV no hay aviso desde la primera descarga; con
OV la reputación se gana con las descargas. Decidir cuando haya varios clientes.

## Cloudflare

El sitio está detrás de Cloudflare (certificado de origen). Los `.exe` entran en las extensiones que Cloudflare cachea por
defecto; por eso nginx responde `Cache-Control: no-cache` en la carpeta y `no-store` en `releases.win.json`. Si alguna vez
se descarga un instalador viejo, purgar la caché de `/downloads/windows/*` en Cloudflare.

## Desviaciones

- **Dominio**: se pensó `descargas.iris.atmosferast.com`; no entra en `*.atmosferast.com` (ver Decisiones). Se usa `/downloads`.
- **Versionado**: el plan inicial usaba la etiqueta de git; ahora es el archivo `VERSION` + empujar a `production`.
