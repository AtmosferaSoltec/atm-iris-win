# 11 · Paridad con el iPad y diseño de escritorio

> **Estado: programada en una Mac, sin compilar el XAML ni abrir la app.** Lo que sí se verificó en la Mac:
> las 88 pruebas de `Core/`, la compilación de todo el C# de la app y cada `{x:Bind}` y `{StaticResource}` del XAML
> contra los tipos compilados (`Scripts/MacCheck`). Lo que falta se hace en Windows con la lista de abajo.

## Objetivo

Que la consola de Windows haga todo lo que hace el iPad, con el mismo aspecto, pero aprovechando la pantalla grande:
el iPad se queda en proyectar letras, música y multimedia; Windows hará más con el tiempo (por eso la barra de
herramientas).

## Qué cambió

### Contrato (`docs/api-contract.md`, copia idéntica de la API)

| Tema | Contrato | Dónde |
|---|---|---|
| Una cuenta por iglesia, sin roles ni equipo | §3 | `Mapping.ToSession` arma cada sesión como dueña con todos los permisos y sin otras iglesias (igual que el iPad). La API falsa ya no tiene operador ni segunda iglesia. Menú de la cuenta: sin rol ni "El equipo se administra desde la web". |
| Módulos del sistema | §6 `availableModules` | `IModuleSettingsRepository.AvailableModulesAsync`. Módulos e Inicio no muestran lo apagado para todo Iris; Ctrl+B no abre la Biblia si está oculta. **La API falsa trae la Biblia apagada**, como producción (`FakeDb.SystemBibleEnabled`). |
| Proyección | §6 `projection` | `IProjectionSettingsRepository` (`PUT /church/projection` por la cola), `ProjectionFonts` (las 10 claves → fuentes de Windows, tabla de la API), `ProjectionTypography` (lo que ven todas las superficies). |
| Almacenamiento por sección | §6 `storage.breakdown` | `StorageDto.Breakdown` (opcional al decodificar). La API falsa lo calcula. El contador se ve en la web (Ajustes). |
| Servicios sin responsable fijo | §9 | `BlockTemplate` sin `DefaultPersonId`; el editor ya no lo pide; el responsable se elige en cada bloque durante el servicio (`LeaderOption` vive ahora en `Features/LiveConsole`). |
| Letras sin copyright ni importación | §10 | DTO, modelos y API falsa. |
| Música en la nube y descarga bajo demanda | §11 | Ver abajo. `GET /media?kind=image,video` en la API falsa. |

### Música en la nube (§11)

- `MediaCache`: imágenes y fondos se descargan solos; **música y videos solo con `RequestAsync`** (al agregarlos al
  servicio) y se quedan en la PC. Un archivo que ya estaba (o su versión anterior) sigue "pedido" después de reiniciar,
  así que si se reemplaza en la web baja la versión nueva. Lo borrado en la web se elimina al sincronizar.
- `ILibraryRepository.DownloadAsync(ids)`; `ServiceItem.MediaId` une cada elemento con su archivo.
- Agregar al servicio: se puede elegir lo que está "En la nube"; Imágenes y Videos ya no muestran los fondos; la pestaña
  Música explica de dónde vienen las pistas.
- Consola: al agregar, empieza la descarga; el escenario muestra "Descargando… 40 %" y habilita Reproducir cuando el
  archivo llega ("Reintentar descarga" si falla). El archivo se resuelve al reproducir o proyectar
  (`LiveConsoleViewModel.Resolve`), porque los elementos del servicio son inmutables.

### Proyección (§6)

- `ProjectionCanvas.Typography`: con `null` (miniaturas, EN VIVO, TV) sigue `ProjectionTypography.Current` y se
  redibuja cuando cambia; la vista previa de Proyección pasa sus propios ajustes. Tamaño en puntos sobre 1920 de ancho,
  escalado al escenario; la nota al pie en la misma fuente a 0,022/0,046 del cuerpo (igual que el iPad).
  **Por defecto la letra pasa de serif (Sitka) a Segoe UI Variable**, que es la equivalencia de "system" en el contrato.
- La consola aplica los ajustes al cargar y un servicio nuevo empieza con el fondo por defecto (o en negro), como el iPad.
- **Pantalla Proyección dentro de Módulos**: en el iPad es una hoja aparte; en Windows, desde 1200 px, Módulos y
  Proyección van lado a lado (abajo en ventanas angostas). Vista previa en vivo, tipografías dibujadas en su propia
  fuente, tamaño con − / + y atajos, fondo al iniciar un servicio. Cada cambio se guarda al instante, en orden, y vuelve
  atrás si falla.

### Diseño de escritorio de la consola

- **Barra de herramientas** bajo la barra superior: Agregar · Biblia | Limpiar pantalla · Fondo (con el nombre del fondo
  actual). Los mismos atajos de siempre. El lado derecho queda libre para lo que venga.
- **Tres columnas siempre** (rediseño del 2026-10-07): BIBLIOTECA (pestañas Letras · Música · Multimedia, búsqueda; se arrastra al servicio o se agrega con "+" / doble clic) | área de trabajo | SERVICIO arriba y EN VIVO abajo con el mini reproductor (pausa, reproducir, barra de progreso). Se quitó la hoja "Agregar al servicio" y la vista SIGUIENTE; `Ctrl+N` enfoca el buscador de la biblioteca. Mínimo de ventana 1100 px.
  + mini reproductor. Más angosto vuelve al diseño del iPad (EN VIVO bajo SERVICIO).
- El encabezado del área de trabajo conserva ‹ › (Biblia) y Editar; limpiar, fondos y Biblia pasaron a la barra.

### Fase 09 (TV y reproducción)

Ya estaba programada: `DisplayAreaWatcher` para conectar y desconectar en caliente, "Elegir pantalla", nombre real del
monitor, `MediaPlayer` principal (salón y TV) más un espejo silenciado para EN VIVO, controles multimedia de Windows.
**Falta probarla con dos monitores.**

## Desviaciones

- **El iPad va detrás del contrato en dos cosas** que Windows ya sigue: (1) sus plantillas todavía guardan un
  responsable por bloque (`defaultPersonID`), que la API ya no envía; (2) su menú de cuenta muestra el rol y "El equipo
  se administra desde la web". Conviene alinear el iPad.
- Agregar al servicio mantiene **cuatro pestañas** (Letras · Música · Imágenes · Videos, como la spec); el iPad usa tres
  (Multimedia junta imágenes y videos).
- Los fondos en video del contrato no se dibujan todavía en ninguna consola (el iPad tampoco): el selector solo ofrece
  imágenes.

## Verificación en Windows

Hazla en este orden y anota cada punto como ✅ / ❌ con lo que viste. Ante un ❌, corrige y vuelve a compilar.

### 1. Compilación y pruebas

```powershell
git pull
dotnet build Iris.csproj -p:Platform=x64          # sin errores; anota las advertencias nuevas
dotnet test Tests/Iris.Tests.csproj               # 88 pruebas en verde
```

Errores probables (lo que la Mac no puede revisar): el XAML de `LiveConsolePage`, `ModulesPage` y `AddToServiceView`
(estructura, `Setter Target="X.(Grid.Row)"`, `Setter Property="Width" Value="Auto"` del estilo
`IrisPillToggleButtonStyle`), y cualquier `XamlParseException` al abrir una pantalla.

### 2. Modo Fake (sin API): abre la app desde Visual Studio con *Iris (Package)*

1. **Entrar** con `pastor@vidanueva.org` / `vidanueva123`. La primera vez se recrea la API falsa
   (`fake-api.json` con el formato viejo se descarta solo) y su copia local pasa a `iris-fake-v2.db`: si la app pide
   iniciar sesión otra vez, es esperado.
2. **Inicio**: la tarjeta de Módulos no lista la Biblia. Las cifras de la biblioteca no cuentan los fondos.
3. **Módulos** con la ventana ancha (≥ 1200 px): Módulos a la izquierda y Proyección a la derecha; sin interruptor de
   Biblia. Angosta: Proyección abajo.
4. **Proyección**: cambia la tipografía, el tamaño (− / + y atajos) y el fondo; la vista previa cambia al instante. Sal
   y vuelve a entrar: se conserva. Con la app en modo sin conexión (Ctrl+Shift+F12 → simular sin conexión) los cambios
   quedan en la cola y se envían al volver.
5. **Iniciar un servicio** (ventana ≥ 1400 px): barra de herramientas visible; tres columnas; el TV/EN VIVO empieza con
   el fondo elegido en Proyección (o negro con "Ninguno"); el botón dice "Fondo · {nombre}".
6. **Agregar → Letras**: la letra se ve en la tipografía y tamaño elegidos en las tarjetas, EN VIVO y el TV. Envía la
   estrofa 1: SIGUIENTE muestra la 2. → la envía.
7. **Agregar → Música**: "Tono de prueba" aparece "En la nube" y se puede elegir. Al agregarlo, el escenario muestra
   "Descargando… %" y luego "Reproducir"; suena y el mini reproductor avanza con el tiempo real. Cierra la app, vuelve
   a abrir, agrégalo otra vez: está listo al instante (no se descarga de nuevo).
8. **Agregar → Imágenes**: no aparece la imagen marcada como fondo; la otra sí.
9. **Ctrl+B** no abre nada (Biblia oculta). Para probar la Biblia: `FakeDb.SystemBibleEnabled = true`, borra
   `fake-api.json` en la carpeta de datos de la app y vuelve a entrar.
10. Ventana angosta (< 1400 px): la consola vuelve a dos columnas sin cortes.
11. Menú de la cuenta: nombre, correo, iglesia (sin rol), cerrar sesión, cerrar en todos los dispositivos.

### 3. Con dos monitores (fase 09)

1. Conecta y desconecta el segundo monitor con la consola abierta: el chip de la barra pasa entre
   "{monitor} · 1920 × 1080" y "Sin pantalla · Conecta un TV"; la ventana del TV aparece y desaparece sin afectar la
   consola.
2. Letras e imágenes se ven en el TV sin interfaz ni cursor. Un video se ve en el TV y en EN VIVO a la vez, en sincronía.
3. Espacio pausa y reanuda; las teclas multimedia y el panel de volumen de Windows muestran el título.

### 4. Contra la API real (opcional)

Conexión (Ctrl+Shift+F12) → modo Live y la URL de la API. Repite 2–7 con la cuenta de una iglesia real: la Biblia sigue
oculta; la música subida en la web (sección Música) aparece en la pestaña Música.

### 5. Reporte

Escribe el resultado al final de este archivo (sección *Resultado en Windows*): fecha, versión de Windows, cada punto
con ✅/❌, los errores con su mensaje y lo que corregiste. Luego marca la fase 09 y esta en `docs/plans/README.md`.

## Resultado en Windows

**2026-10-07 · Windows 11 Pro 10.0.26200** (sesión sin interfaz: solo compilación, pruebas y API real)

- ✅ 1. Compilación y pruebas: build Debug y Release sin errores; 90 pruebas verdes. Con `obj` viejo el XAML falla con `LeaderOption` (caché): borrar `obj\x64`.
- ✅ 4. Contra la API real (`https://iris-api.atmosferast.com/api/v1`): login, sync completo, `availableModules.bible = false`, `storage.breakdown` presente, 189 canciones y 6 medios decodifican con el stack de la app. Biblia 404 como el contrato.
- ⚠️ 2 y 3 (pantallas, TV, dos monitores): **sin verificar**; hay que hacerlas a mano con las listas de arriba.
- Desviación: el código conserva restos del modelo con roles (ver fase 10, *Desviaciones*).
