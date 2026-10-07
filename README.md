# Iris · consola Windows

Consola de proyección para iglesias: un PC de control maneja un TV o proyector (segundo monitor) que muestra solo el
contenido (letras, versículos, fondos, imágenes y video) mientras suena la música en el salón. WinUI 3, Windows App SDK
2.5, .NET 8, empaquetada como MSIX. Todo el texto visible está en español; el código, en inglés.

## Requisitos

- Windows 11 (mínimo 10.0.17763), Visual Studio 2022 con la carga de trabajo *Desarrollo de la plataforma universal de
  Windows* / *Windows App SDK*, SDK de .NET 8.
- Plataforma de compilación siempre explícita (`x64`, `x86` o `ARM64`; `AnyCPU` no existe).

## Compilar, probar y ejecutar

```powershell
dotnet build Iris.csproj -p:Platform=x64               # Debug
dotnet build Iris.csproj -c Release -p:Platform=x64    # Release (recorte activo)
dotnet test  Tests/Iris.Tests.csproj                   # pruebas xUnit
```

Ejecutar: desde Visual Studio con el perfil **Iris (Package)**. El `Iris.exe` de un build normal es la app empaquetada y
se cierra si se lanza a mano. Para una copia sin empaquetar, ver `CLAUDE.md`.

> Si el build falla con errores de XAML que mencionan tipos que sí existen (p. ej. `LeaderOption`), borra `obj\x64` y
> vuelve a compilar: es una caché de XAML vieja.

## Modos de datos y conexión

| Modo | Qué es |
|---|---|
| **Mock** | Datos de ejemplo en memoria (diseño y pruebas de interfaz). |
| **Fake** | El código real (red, tokens, copia SQLite, cola) contra una API falsa dentro de la app. Se elige en Conexión. Cuenta: `pastor@vidanueva.org` / `vidanueva123`. |
| **Live** | La API real. Es el modo por defecto (Debug y Release), apuntando a `https://iris-api.atmosferast.com/api/v1`. |

`Ctrl+Shift+F12` abre el diálogo **Conexión**: elige el modo, escribe la URL y pulsa **Probar**.

- Servidor desplegado: `https://iris-api.atmosferast.com/api/v1`
- API local (si la levantas tú): `http://localhost:3020/api/v1`; desde otro equipo, `http://<IP>:3020/api/v1`.

La primera vez en Live la app descarga la copia completa de la iglesia; después trabaja sin conexión y sincroniza
sola (ver `docs/plans/02-sincronizacion`).

## Arquitectura

```
Core/Networking   ApiClient, DTO con JSON generado (sin reflexión), API falsa (Fake/)
Core/Auth         Sesión, refresh de tokens (single flight), almacenamiento seguro
Core/Persistence  Copia local SQLite y cola de escrituras
Core/Sync         SyncEngine, Outbox
Core/Media        Caché de archivos (música y videos solo una vez agregados al servicio)
Core/Bible        Biblia RVR1909 sin conexión (hoy apagada por el sistema)
Core/Services     Interfaces + Mocks/ + Live/
DesignSystem/     Tokens, estilos y controles Iris*
Features/         Auth, Home, LiveConsole, Bible, Modules, Services, People, Times, Connection…
Shared/Projection ProjectionCanvas: el único renderizador de miniaturas, EN VIVO y TV
Shell/            Composición, SessionStore, segundo monitor
```

Reglas del proyecto, MVVM y estilo: `CLAUDE.md`. Plan por fases y desviaciones: `docs/plans/README.md`. Contrato de la
API: `docs/api-contract.md`. Spec de diseño: `docs/IRIS_SPEC.md`.

## Pruebas

`Tests/` enlaza todo `Core/` (sin WinUI): lógica pura, la API falsa contra el contrato, sincronización, cola, tiempos.

**Pruebas contra el servidor real** (`Tests/LiveApiTests.cs`): se activan con variables de entorno; sin ellas pasan sin
hacer nada.

```powershell
$env:IRIS_LIVE_URL      = "https://iris-api.atmosferast.com/api/v1"
$env:IRIS_LIVE_EMAIL    = "<correo de la cuenta>"
$env:IRIS_LIVE_PASSWORD = "<contraseña>"
dotnet test Tests/Iris.Tests.csproj --filter LiveApiTests
```

Usan el stack real de la app (cliente HTTP, refresh de tokens, `SyncEngine`, SQLite): contraseña incorrecta, login,
sincronización completa, refresh, crear persona idempotente y duplicada (se borra al final) y Biblia apagada. El servidor
limita el login a 5 por minuto por IP, así que no repitas la corrida más de unas pocas veces seguidas.

## Si no conecta con una API local

Misma red, firewall abierto para el puerto 3020 y la URL con la IP del equipo que corre la API. Las URLs de archivos
dependen de `STORAGE_PUBLIC_ENDPOINT` en la API.
