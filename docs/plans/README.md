# Planes de implementación · atm-iris-win (consola Windows)

> **Para el agente de Windows.** Este es tu punto de partida. Trabajas en la **PC con Windows**,
> solo en este repo, las fases en orden y sin detenerte entre ellas (reglas completas en
> [`00-fundamentos/plataforma.md`](00-fundamentos/plataforma.md) §4).
>
> La maqueta WinUI 3 está terminada. Ahora se conecta a la API real, funciona sin conexión,
> proyecta en el segundo monitor y reproduce audio y video de verdad.
>
> **No tienes la API durante el desarrollo**: corre en la Mac, que también la está construyendo.
> Por eso la fase 00 crea una **API falsa dentro de la app** que cumple el contrato; con ella pruebas
> todo el código real (red, tokens, sincronización, cola). La conexión a la API verdadera la hace el
> revisor al final.

## Lee antes de empezar

1. [`00-fundamentos/plataforma.md`](00-fundamentos/plataforma.md): producto, arquitectura y reglas de trabajo.
2. [`../api-contract.md`](../api-contract.md): el contrato de la API, **al pie de la letra**.
3. [`../IRIS_SPEC.md`](../IRIS_SPEC.md): diseño, pantallas y reglas (copia de la spec del iPad; §12 es la adaptación a Windows).
4. [`../../CLAUDE.md`](../../CLAUDE.md): reglas del proyecto (MVVM, tokens, controles `Iris*`, compilar con plataforma).
   Donde `CLAUDE.md` diga que no hay API ni persistencia, o que hay que pedir aprobación antes de pantallas nuevas,
   **manda este plan**: las fases ya están aprobadas.

## Verificación

- **En cada fase**: `dotnet build Iris.csproj -p:Platform=x64` sin errores ni advertencias nuevas, y abrir la app
  (perfil *Iris (Package)* o la copia sin empaquetar de `CLAUDE.md`) para mirar lo que cambiaste en modo **Fake**.
- **Pruebas** (xUnit en `Tests/`): solo en la fase 10.

## Fases

| # | Fase | Estado |
|---|---|---|
| 00 | [Fundamentos](00-fundamentos/README.md): modos de datos, cliente HTTP, DTO con JSON generado, API falsa, limpieza | [ ] |
| 01 | [Login](01-login/README.md): acceso real, sesión permanente, recuperación en 3 pasos | [ ] |
| 02 | [Sincronización](02-sincronizacion/README.md): copia local SQLite, feed de cambios, cola de escrituras, conexión | [ ] |
| 03 | [Cuenta y permisos](03-cuenta-y-permisos/README.md): cambio de iglesia, cerrar sesión, permisos en cada pantalla | [ ] |
| 04 | [Iglesia](04-iglesia/README.md): módulos, personas y tipos de servicio sobre la copia local | [ ] |
| 05 | [Canciones](05-canciones/README.md): biblioteca de letras real | [ ] |
| 06 | [Multimedia](06-multimedia/README.md): caché de archivos, fondos personalizados, biblioteca real | [ ] |
| 07 | [Biblia](07-biblia/README.md): RVR1909 completa sin conexión | [ ] |
| 08 | [Tiempos](08-tiempos/README.md): guardar, ajustar y consultar registros | [ ] |
| 09 | [TV y reproducción](09-tv-y-reproduccion/README.md): segundo monitor en caliente, audio y video reales | [ ] |
| 10 | [Calidad y entrega](10-calidad-y-entrega/README.md): pruebas, limpieza, documentación, reporte | [ ] |

Marca cada casilla al terminar la fase y completa su sección *Desviaciones*.

## Reglas propias de este repo

- Las de `CLAUDE.md` (MVVM con CommunityToolkit.Mvvm, sin valores literales en vistas, controles `Iris*`, compilar con plataforma).
- Datos solo por las interfaces de `Core/Services`. Las implementaciones reales se llaman `Live…` y conviven con los `Mock…`.
- **Nada de red en vistas ni ViewModels**: los ViewModels hablan con los repositorios; los repositorios leen la copia local y
  escriben por la cola. Solo `Core/Networking`, `Core/Auth` y `Core/Sync` hablan con la API.
- **Recorte (trimming) activo en Release** (`PublishTrimmed`): todo JSON con `System.Text.Json` **generado por código**
  (`JsonSerializerContext`), sin reflexión; nada de librerías que dependan de reflexión (EF Core, Newtonsoft).
- Cada fase que agrega endpoints también los agrega a la **API falsa** (fase 00), con el mismo comportamiento del contrato.
- Textos en español como la spec; los mensajes de error de la API se muestran tal cual. Fechas en la **zona de la iglesia**.
