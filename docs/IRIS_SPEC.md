# Iris — Especificación de diseño y producto

> Documento de traspaso de la maqueta de **iPad (SwiftUI)** para replicarla como **app de escritorio Windows 11** (recomendado: **WinUI 3 + Windows App SDK, C#/.NET 8, MVVM**).
> Contiene todo lo construido hasta ahora: concepto, tokens de diseño, componentes, pantallas, comportamiento, arquitectura, datos de prueba y textos visibles.
> Todo lo que se ve en pantalla está en **español**; el código va en **inglés**.
>
> **Estado: maqueta funcional.** Todas las pantallas y reglas funcionan, pero **nada está conectado a un servidor**: el login acepta cualquier correo, los datos salen de mocks en memoria (§10–11) y se pierden al cerrar el app, la música y el video se simulan, y no hay salida real al TV. Hay que replicar exactamente eso (mismas interfaces y mocks) para que luego baste con cambiar los mocks por implementaciones reales.
>
> **Recursos del logo**: se entregan aparte en la carpeta **`iris-logo-seleccionado`** (en Descargas). Copia al proyecto de Windows lo que necesites; el detalle de cada archivo está en §5.2.

---

## 0. Índice

1. [Producto](#1-producto)
2. [Principios de la maqueta](#2-principios-de-la-maqueta)
3. [Dirección visual](#3-dirección-visual)
4. [Tokens de diseño](#4-tokens-de-diseño)
5. [Componentes base](#5-componentes-base)
6. [Pantallas](#6-pantallas)
7. [Reglas de comportamiento](#7-reglas-de-comportamiento)
8. [Arquitectura](#8-arquitectura)
9. [Modelos de datos](#9-modelos-de-datos)
10. [Servicios (interfaces) y mocks](#10-servicios-interfaces-y-mocks)
11. [Datos de prueba](#11-datos-de-prueba)
12. [Adaptación a Windows 11](#12-adaptación-a-windows-11)
13. [Inventario de textos](#13-inventario-de-textos)
14. [Checklist de paridad](#14-checklist-de-paridad)

---

## 1. Producto

**Iris** proyecta en el TV de la iglesia, durante el servicio, **letras de canciones, versículos, fondos, imágenes, videos y música**.

- El **dispositivo de control** (iPad hoy, PC con Windows 11 ahora) es la **consola**. La versión de iPad es **solo para iPad** (no iPhone, no Mac, no Vision Pro) y está **diseñada solo para horizontal** (ver principio 6).
- El **TV / proyector** muestra **solo el contenido**: sin controles, sin interfaz.
- V1 es **local** con datos de prueba; más adelante se conectará a un **API**. Por eso todo dato pasa por **interfaces con implementaciones mock**.

En Windows, el TV es una **segunda ventana a pantalla completa en el monitor secundario** (ver §12.6).

---

## 2. Principios de la maqueta

1. **Vistas sin lógica.** Las vistas solo pintan estado y reenvían intenciones al ViewModel.
2. **Estado observable** en ViewModels (en WinUI: `ObservableObject` de *CommunityToolkit.Mvvm*, `[ObservableProperty]`, `[RelayCommand]`).
3. **Datos detrás de interfaces** (`IAuthService`, `IServicePlanRepository`, …) con implementaciones `Mock*`. Un único **composition root** (`AppDependencies.Mock`) que luego se cambia por `Live`.
4. **Componentes reutilizables** con prefijo `Iris*` y **tokens** centralizados. Nada de colores/medidas sueltos en las vistas.
5. **Un mismo renderizador de proyección** (`ProjectionCanvas`) para miniaturas, vista previa EN VIVO y la salida al TV → se ven idénticos.
6. **Diseño solo horizontal, sin bloquear la orientación.** Cada pantalla tiene un único diseño horizontal; no existen diseños verticales ni "angostos". Desde iPadOS 26/27 una app ya no puede bloquear su orientación: `UIRequiresFullScreen` está obsoleto (Xcode da un aviso) y con el SDK de iOS 27 el sistema lo ignora, igual que la lista de orientaciones, porque toda app de iPad es una ventana redimensionable (nota técnica TN3192). Por eso, siguiendo a Apple:
   - `UISupportedInterfaceOrientations~ipad` declara las **4 orientaciones** y **no** se usa `UIRequiresFullScreen`. Cero avisos.
   - **Lienzo horizontal escalado** (`IrisLandscapeCanvas`, raíz de la app): la interfaz siempre se maqueta en un lienzo horizontal de al menos `landscapeCanvas` (1100 × 640). Si el área segura de la ventana es más chica (iPad en vertical, ventana angosta), todo el diseño horizontal se **reduce proporcionalmente** y se centra, con el fondo `IrisBackground` en las bandas de arriba y abajo (como un televisor "letterbox"). El alto del lienzo nunca supera la proporción horizontal del propio dispositivo (máximo 4:3), así que en vertical se ve una copia exacta en pequeño, no un diseño estirado.
   - Nada se reacomoda ni se deforma, y la consola **nunca se bloquea**: si alguien gira el iPad sin querer en pleno servicio, el operador sigue controlando todo (el estado — servicio, cronómetro — se conserva al girar). Se descartó un aviso "Gira el iPad" porque taparía la consola en vivo y sería un mensaje equivocado en una ventana angosta con el iPad horizontal.
   - En horizontal en cualquier iPad (el mini tiene 1133 × 700 de área segura) la escala es 1: se ve a tamaño real.
   - Las hojas, menús y alertas del sistema no se escalan (las presenta iPadOS a su tamaño normal).
   - Ventana mínima `minimumWindow` (744 × 560) con `.windowResizability(.contentMinSize)`: el lienzo nunca baja de ~2/3 de su tamaño.
   - Verificado en el simulador iPad mini (A17 Pro), iOS 27, compilación Debug: horizontal, vertical y vertical invertido.

---

## 3. Dirección visual

**Concepto: "luz que atraviesa un vitral".**

- **Oscuro siempre** (dark-first, sin modo claro). La consola se usa en templos con poca luz y no debe competir con el TV.
- **Espectro cálido → frío** como acento: ámbar → coral → rosa → violeta → índigo.
- **Serif editorial** para títulos y para el contenido proyectado (letras/versículos); **sans** para la interfaz.
- **Superficies esmeriladas** (paneles) para contenido; **vidrio** (Liquid Glass en iOS → *Acrylic/Mica* en Windows) solo para controles flotantes (botones redondos, cápsulas, chips de estado).
- Minimalista: mucho negro, bordes de 1 px muy tenues, un solo acento de color por estado.
- Movimiento calmado: transiciones suaves (fundidos, "blur replace"), nunca rebotes llamativos. Respeta "Reducir movimiento".

---

## 4. Tokens de diseño

### 4.1 Color (`IrisColor`)

| Token | Valor | Uso |
|---|---|---|
| `canvas` | `#07070B` | Fondo base de toda la app |
| `canvasElevated` | `#0E0E15` | Fondo de modales / hojas / paneles (mezclado al 72 %) |
| `surface` | `#FFFFFF` @ 5 % | Relleno de campos, filas, celdas |
| `surfaceRaised` | `#FFFFFF` @ 8 % | Campo enfocado, fila seleccionada |
| `stroke` | `#FFFFFF` @ 9 % | Bordes por defecto (1 px) |
| `strokeStrong` | `#FFFFFF` @ 18 % | Bordes de la vista EN VIVO, checkmark vacío |
| `textPrimary` | `#F6F3EE` | Texto principal (blanco cálido) |
| `textSecondary` | `#F6F3EE` @ 64 % | Texto secundario |
| `textTertiary` | `#F6F3EE` @ 40 % | Placeholders, metadatos, contadores |
| `textInverse` | `#120D0A` | Texto sobre rellenos claros o de acento |
| `ember` | `#FFB547` | Espectro 1 · color de **Letra** |
| `coral` | `#FF7A59` | Espectro 2 · **acento principal**, cursor, sliders, **Imagen** |
| `rose` | `#F0508C` | Espectro 3 · **Video** |
| `violet` | `#9B5CFF` | Espectro 4 · **Pasaje** (Biblia) |
| `indigo` | `#4E5BFF` | Espectro 5 · **Anuncio** |
| `glowViolet` | `#2A1658` | Brillo ambiental del fondo |
| `glowIndigo` | `#131E5C` | Brillo ambiental del fondo |
| `glowRose` | `#3D1235` | Brillo ambiental del fondo |
| `glowEmber` | `#3A1E08` | Brillo ambiental del fondo |
| `success` | `#3DDC97` | TV conectado, **Música** |
| `warning` | `#FFC857` | TV sin conectar |
| `danger` | `#FF5C7A` | Errores de validación, acciones destructivas |
| `live` | `#FF3B5C` | Punto pulsante "EN VIVO" |

### 4.2 Degradados (`IrisGradient`)

| Token | Definición | Uso |
|---|---|---|
| `accent` | Lineal izq→der: `ember → coral → rose` | Botón primario, borde de selección, borde de campo enfocado, palabra destacada del hero, avatar, checkmark |
| `spectrum` | `ember, coral, rose, violet, indigo` | Colores rotativos de avatares, bloques (`BlockTimeline`) y filas de resúmenes (`spectrum[i % 5]`) |

El logo **no** se dibuja con degradados en código: son archivos SVG/PNG (§5.2).

### 4.3 Tipografía (`IrisFont`)

iPad usa **New York** (serif) y **SF Pro** (sans). En Windows:

- Sans → **Segoe UI Variable** (Text / Display según tamaño).
- Serif → **Sitka** (incluida en Windows: *Sitka Display* para títulos, *Sitka Text* para cuerpo), o empaquetar una serif libre (**Newsreader** o **Source Serif 4**, licencia OFL) si se quiere un acabado más cercano a New York.

| Token | iPad (pt) | Peso | Familia | Windows sugerido (epx) | Uso |
|---|---|---|---|---|---|
| `display` | 56 | Medium | Serif | 56 | Titular del hero del login |
| `headline` | 34 (largeTitle) | Medium | Serif | 34 | Títulos de pantalla (Módulos, Personas, Servicios, Tiempos) |
| `title` | 28 (title) | Medium | Serif | 28 | Títulos de panel/modal ("Te damos la bienvenida", "Sublime gracia") |
| `projection` | 26 | Regular | Serif | 26 | Letra en el mini TV del login |
| `subtitle` | 20 (title3) | Regular | Sans | 18 | Subtítulo del hero |
| `body` | 17 | Regular | Sans | 15 | Texto de campos |
| `bodyEmphasized` | 17 | Semibold | Sans | 15 | Botones primarios, título de servicio |
| `callout` | 16 | Regular | Sans | 14 | Descripciones |
| `calloutEmphasized` | 16 | Semibold | Sans | 14 | Títulos de filas, segmentos, links |
| `label` | 13 (footnote) | Medium | Sans | 13 | Etiquetas de campo, nombre de diapositiva |
| `caption` | 12 | Medium | Sans | 12 | Metadatos, chips, errores |
| `overline` | 11 (caption2) | Bold | Sans, **MAYÚSCULAS** | 11 | Encabezados de sección ("SERVICIO", "FONDOS"), badges |

Tracking (`IrisTracking`): `tight = -0.5` (titulares serif), `overline = 2.0` (encabezados), `caps = 1.2` (badges "EN VIVO").
Números (reloj, duraciones, índices): **monoespaciados / tabular**.

**Texto proyectado (`ProjectionCanvas`)** escala con el ancho `W` del lienzo 16:9:
- Letra/versículo: serif Medium, `W × 0.046`, interlineado `W × 0.006`, centrado, factor de reducción mínimo 0.5, sombra negra 45 % radio `W × 0.012`, padding `W × 0.07`.
- Pie (referencia, p. ej. "SALMOS 23:1"): sans Semibold `W × 0.022`, mayúsculas, tracking `W × 0.003`, opacidad 60 %.

### 4.4 Espaciado (`IrisSpacing`) — escala de 4

`xxs 4 · xs 8 · sm 12 · md 16 · lg 24 · xl 32 · xxl 48 · xxxl 64`

### 4.5 Radios (`IrisRadius`) — siempre esquinas continuas/suaves

`sm 10 · md 16 · lg 22 · xl 28 · xxl 36`
(En WinUI: `CornerRadius`; no existen "continuous corners", usar los mismos valores.)

### 4.6 Tamaños (`IrisSize`)

| Token | Valor |
|---|---|
| `controlHeight` | 56 (iPad) → **44** en Windows (densidad de escritorio) |
| `segmentHeight` | 44 → **36** |
| `iconButton` | 44 → **36** (zona de clic ≥ 32) |
| `authPanelWidth` | 480 |
| `readableWidth` | 560 |
| `settingsColumnWidth` | 720 (Módulos, Personas) |
| `contentWidth` | 1280 (Inicio, Servicios, Tiempos) |
| `landscapeCanvas` | 1100 × 640 (tamaño mínimo al que se maqueta el diseño horizontal; más chico, se escala — principio 6) |
| `landscapeCanvasMaxAspect` | 0.75 (alto ÷ ancho máximo del lienzo escalado: 4:3) |
| `minimumWindow` | 744 × 560 (tamaño mínimo de la ventana, `contentMinSize`) |

### 4.7 Movimiento (`IrisMotion`)

| Token | iOS | Equivalente sugerido |
|---|---|---|
| `snappy` | snappy 0.30 s | 200–250 ms, curva ease-out |
| `smooth` | smooth 0.45 s | 350–450 ms, curva ease-in-out |
| `gentle` | spring 0.8 s, bounce 0.1 | 600–800 ms, ease-out suave |

Transiciones usadas: **fundido** (opacity), **blur replace** (fundido + desenfoque), **mover desde abajo + fundido** (toasts, mini reproductor), **escala 0.97–0.98 al presionar** (botones y tarjetas).

### 4.8 Sombras

| Elemento | Sombra |
|---|---|
| Panel (`IrisSurface`) | negro 45 %, radio 40, y +24 |
| Botón primario | coral 40 %, radio 22, y +10 |
| Tarjeta seleccionada | coral 30 %, radio 16, y +4 |
| Campo enfocado | coral 22 %, radio 18 |
| Mini TV del login | violeta 30 %, radio 60, y +30 |

---

## 5. Componentes base

> Nombres de iPad → equivalente WinUI 3 sugerido.

### 5.1 `IrisBackground` — fondo ambiental
- Malla 3×3 de colores (MeshGradient) sobre `canvas`:
  ```
  glowViolet   canvas   glowIndigo
  canvas       canvas   canvas
  glowRose     glowEmber canvas
  ```
- Los vértices internos se desplazan lentamente con senos desfasados (amplitud 0.10–0.14, frecuencias 0.13–0.21 rad/s).
- **Viñeta**: degradado radial de transparente (r = 240) a `canvas` @ 70 % (r = 1000).
- Parámetro `isAnimated`. **Login y consola usan la versión estática** (la animación se percibía como "temblor"). Reducir movimiento → siempre estático.
- **Windows**: Win2D/Composition (`CompositionRadialGradientBrush` superpuestos) o una imagen pre-renderizada de alta resolución; la versión estática basta.

### 5.2 `IrisMark` / `IrisWordmark` / icono / splash — logo

El logo es un **anillo de espectro abierto** (ámbar → coral → rosa → violeta → índigo, con una abertura arriba a la izquierda) y un **punto blanco** (`#F6F3EE`) dentro, como el brillo de un ojo. El logotipo es "iris" en minúsculas (Montserrat Medium **convertida a trazos**: no hace falta instalar la fuente).

**Archivos** (carpeta `iris-logo-seleccionado` en Descargas; en el iPad están en `Assets.xcassets`):

| Archivo | Recurso iPad | Uso | Windows |
|---|---|---|---|
| `AppIcon-1024.png` | `AppIcon` | Icono de la app. 1024×1024, a sangre, sin transparencia, fondo `#0B0B10`. | Generar desde él los logos del paquete MSIX (`Square44x44Logo`, `Square150x150Logo`, `StoreLogo`, etc., con el asistente de *Visual Assets* del manifiesto) y el `.ico` de la ventana. |
| `iris-anillo.svg` | `IrisRing` | Capa del anillo (caja 1000×1000, fondo transparente). | `SvgImageSource` en un `Image`. |
| `iris-brillo.svg` | `IrisGlint` | Capa del punto blanco, **misma caja 1000×1000** para que encaje exacto sobre el anillo. | Ídem. |
| `iris-logo.svg` | `IrisLogo` | Símbolo + "iris" en una sola imagen (proporción 2.564 : 1), texto claro para fondo oscuro. | Ídem. |

No se usan los demás archivos del kit original (versiones claras, monocromas, PNG de varios tamaños).

- **IrisMark** (tamaño `S`): `IrisRing` y `IrisGlint` superpuestos en la misma caja `S × S`. **Estático en toda la app.** Se usa en: tarjeta principal del Inicio (520 al 20 %), mini TV con el logo de la iglesia y el contenido `logo(name)` de `ProjectionCanvas`.
- **IrisWordmark**: la imagen `IrisLogo` escalada con alto = `markSize` (30 por defecto; 24 en la barra superior). Se usa en el hero del acceso y en la barra superior.
- **Splash** (`IrisSplashView`) — **el único lugar donde el logo se anima**, y solo al abrir el app (ver §6.0).
- **Windows**: `IrisMark` = `UserControl` con un `Grid` de dos `Image` (anillo y brillo) del mismo tamaño; `IrisWordmark` = un `Image` con `iris-logo.svg` y `Height` fijo.

### 5.3 `IrisTextField` — campo de texto
- Etiqueta encima (`label`, `textSecondary`), luego contenedor de 56 (44 Win) de alto, radio `md`, relleno `surface` (`surfaceRaised` si tiene foco).
- Icono a la izquierda (22 de ancho): `textTertiary` → `textPrimary` con foco → `danger` con error.
- Borde: `stroke` 1 px → **degradado `accent` 1.5 px con foco** → `danger` 80 % 1.5 px con error. Sombra coral 22 % con foco.
- Placeholder `textTertiary`; cursor `coral`.
- Contraseña: botón ojo (`eye` / `eye.slash`) para mostrar/ocultar.
- Debajo: error (icono `exclamationmark.circle.fill` + texto, `danger`, aparece con fundido) **o** pista (`textTertiary`).
- Tipos: `text`, `name`, `organization`, `email`, `password`, `newPassword` (autocapitalización y teclado según tipo).
- **Windows**: `TextBox` / `PasswordBox` (con `PasswordRevealMode`) restyleado vía `ControlTemplate`.

### 5.4 Botones
| Estilo | Aspecto | WinUI |
|---|---|---|
| `irisPrimary(isLoading)` | Cápsula ancho completo, alto 56/44, relleno `accent`, texto `textInverse` semibold, borde blanco 35 % (overlay), sombra coral. Cargando: oculta texto y muestra spinner. Deshabilitado: opacidad 45 %. | `Button` con estilo propio + `ProgressRing` |
| `irisGlass` | Cápsula ancho completo, vidrio interactivo, texto `textPrimary` | `Button` con fondo `AcrylicBrush` |
| `irisIcon(isActive)` | Círculo 44/36 de vidrio con un icono. **Activo**: vidrio teñido coral, icono relleno en `textInverse` | `Button` redondo / `ToggleButton` |
| `irisPill` | Cápsula pequeña (alto 36/32) de vidrio, texto `label` | `Button` compacto |
| `irisLink` | Solo texto `calloutEmphasized`, `textSecondary` → `textPrimary` al presionar | `HyperlinkButton` restyleado |
| `irisPressable` | Sin estilo; escala 0.97 y opacidad 85 % al presionar | `Button` con contenido libre |

### 5.5 `IrisSegmentedControl`
- Cápsula `surface` con borde `stroke`, padding 4.
- Segmento seleccionado: cápsula **`textPrimary` (blanco cálido) con texto `textInverse`**, que **se desliza** al cambiar (matched geometry). No seleccionados: texto `textSecondary`.
- Alto de segmento 44/36, texto `calloutEmphasized`.
- **Windows**: `Segmented` del Windows Community Toolkit o `RadioButtons`/`SelectorBar` restyleados con indicador animado.

### 5.6 `IrisSurface` — panel
- Radio por defecto `xxl` (36); en la consola `xl` (28).
- Fondo: material ultrafino + `canvasElevated` @ 72 %.
- Borde 1 px con degradado vertical blanco 16 % → 3 %.
- Sombra negra 45 % radio 40 y +24.
- **Windows**: `Border` con `AcrylicBrush` (tinte `#0E0E15`, opacidad de tinte ~0.72) y borde con `LinearGradientBrush`.

### 5.7 `IrisBanner`
- Fila: icono + mensaje. Estilos `error` (`danger`, `exclamationmark.triangle.fill`), `success` (`success`, `checkmark.circle.fill`), `info`.
- Fondo tinte 12 %, borde tinte 35 %, radio `md`.
- **Windows**: `InfoBar` restyleado.

### 5.8 `IrisChip`
- Cápsula con icono opcional + texto `caption`, color del tinte, fondo tinte 14 %, borde tinte 25 %.
- Usado para el tipo del elemento ("Letra", "Video"…).

### 5.9 `IrisSectionHeader`
- Texto `overline` en MAYÚSCULAS, tracking 2, `textTertiary`, con accesorio opcional a la derecha (contador, menú "•••").

### 5.10 `IrisLiveDot` / `IrisLiveIndicator`
- **Dot**: círculo `live` de 7 px que pulsa (opacidad 1 → 0.3 y escala 1 → 0.8, ease-in-out 0.9 s, en bucle).
- **Indicator**: dot + texto `overline` ("EN VIVO", "EN PANTALLA", "REPRODUCIENDO") en cápsula de vidrio.

### 5.11 `IrisCheckmark`
- Círculo de 24. Apagado: borde `strokeStrong` 1.5 + relleno negro 25 %. Encendido: relleno `accent` + check `textInverse` bold (aparece con escala).

### 5.12 `ProjectionCanvas` (compartido) — **pieza clave**
Renderiza un `ProjectionFrame` en 16:9 a cualquier tamaño:
- Capa 1: negro.
- Capa 2: fondo (`ProjectionBackground`) si existe → degradado lineal diagonal de sus colores + radial blanco 14 % arriba + velo negro 20 % (contraste).
- Capa 3: contenido:
  - `blank` → nada.
  - `text(body, footnote)` → letra serif + referencia (ver §4.3).
  - `image(title, artwork)` → imagen a pantalla completa (placeholder: degradado del artwork + brillo radial).
  - `video(title, duration)` → círculo con ▶ + título serif + duración (placeholder hasta tener video real).
  - `audio(title, duration)` → icono de forma de onda verde + título + duración (solo en tarjetas; nunca va al TV).
  - `logo(name)` → IrisMark + nombre de la iglesia.
- Cambios de contenido/fondo: **fundido cruzado**.
- **Windows**: `UserControl` con `Viewbox`/tamaños proporcionales al `ActualWidth`. **La misma clase** se usa en miniaturas, EN VIVO y la ventana del TV.

---

## 6. Pantallas

### 6.0 Splash (al abrir el app)

- Cubre toda la ventana por encima del acceso/Inicio: fondo `canvas` y `IrisMark` de 160 centrado.
- Animación (≈ 1.6 s en total):
  1. El anillo arranca rotado **−160°**, a escala **0.82** y opacidad 0, y llega a 0°, escala 1 y opacidad 1 con un resorte de **1.1 s** (rebote 0.15). Escala y opacidad afectan al logo completo; la rotación, **solo al anillo**.
  2. El brillo (fijo, nunca gira) aparece con un fundido de 0.35 s que empieza a los **0.75 s**.
  3. A los **1.6 s** el splash se quita con un fundido `smooth` (0.45 s) y deja ver la pantalla que está debajo (acceso o Inicio).
- Con "Reducir movimiento" (Windows: "Efectos de animación" desactivado): sin giro ni escala, solo un fundido de 0.4 s y la misma salida.
- La pantalla de arranque del sistema (antes del splash) es negra; no lleva logo.
- **Windows**: una capa (`Grid`) sobre el contenido de `MainWindow`, animada con `Storyboard` o Composition (`RotationAngleInDegrees` del anillo, `Scale` y `Opacity` del conjunto).

### 6.1 Acceso (login / crear cuenta / recuperar)

**Layout** (único, horizontal): dos columnas con padding lateral 64.
- **Izquierda (hero)**, alineado a la izquierda:
  - `IrisWordmark` arriba.
  - Espacio flexible.
  - Overline con degradado: **"PROYECCIÓN PARA IGLESIAS"**.
  - Titular `display`: **"Que cada palabra *ilumine* el templo."** ("ilumine" con degradado `accent`).
  - Subtítulo: "Letras, versículos y multimedia en la pantalla grande. Tú diriges todo desde el iPad." (Windows: "…desde tu computadora.")
  - **Mini TV** (`ProjectionPreview`, máx. 440 de ancho, 16:9): fondo negro con radial violeta arriba, badge "EN PANTALLA" con punto pulsante, letra serif + referencia en mayúsculas. **Rota cada 5 s** entre 4 ejemplos con transición blur replace. Sombra violeta.
  - Espacio flexible.
- **Derecha**: panel `IrisSurface` de 480 de ancho, centrado verticalmente (con scroll si no cabe).

**Panel**:
1. Título (`title`) + subtítulo (cambian con fundido según modo):
   - Iniciar sesión: "Te damos la bienvenida" / "Ingresa con el correo de tu iglesia para preparar el servicio."
   - Crear cuenta: "Crea el espacio de tu iglesia" / "Configúralo en menos de un minuto y empieza a proyectar."
2. `IrisSegmentedControl`: **Iniciar sesión | Crear cuenta**.
3. Banner de error (si lo hay).
4. Formulario:
   - **Iniciar sesión**: "Correo de la iglesia" (placeholder `nombre@tuiglesia.org`), "Contraseña" ("Tu contraseña"), link derecho **"¿Olvidaste tu contraseña?"**.
   - **Crear cuenta**: "Nombre de la iglesia" (`Iglesia Vida Nueva`, icono edificio), "Responsable" (`Nombre y apellido`, icono persona), "Correo de la iglesia", "Contraseña" (`Crea una contraseña`, pista "Mínimo 8 caracteres.").
   - Enter avanza al siguiente campo; en el último envía.
5. Botón primario: **"Entrar"** / **"Crear cuenta"** (con spinner mientras carga).
6. Solo en crear cuenta: "Al crear tu cuenta aceptas los **Términos** y la **Política de privacidad**." (links, centrado, `caption`).

**Validación** (en el ViewModel):
- ⚠️ **Fase de maqueta: "Entrar" está libre** (no valida ni exige correo; si va vacío, el mock devuelve `pastor@vidanueva.org`). Restaurar validación al conectar el API.
- Crear cuenta: requeridos → "Escribe el nombre de tu iglesia.", "Escribe el nombre del responsable."; correo → "Ingresa el correo de tu iglesia." / "Ese correo no parece válido."; contraseña → "Ingresa tu contraseña." / "Usa al menos 8 caracteres."
- Al fallar la validación se enfoca el primer campo con error. Editar un campo borra su error y el banner.
- Errores del servicio: "El correo o la contraseña no coinciden. Revísalos e inténtalo de nuevo." · "Ya existe una cuenta con ese correo. Intenta iniciar sesión." · "No pudimos conectarnos. Verifica tu conexión a internet." · genérico "Algo salió mal. Inténtalo de nuevo."

**Recuperar contraseña** (modal tamaño formulario, fondo `canvasElevated`):
- Emblema circular 64 con icono `key.fill` (degradado `accent`) → cambia a `envelope.open.fill` al enviar. Botón cerrar (icono `xmark`).
- Editando: "Recupera tu acceso" / "Escribe el correo de tu iglesia y te enviaremos un enlace para crear una nueva contraseña." + campo correo (precargado con el del login) + botón **"Enviar enlace"**.
- Enviado: "Revisa tu correo" / "Enviamos un enlace a **{correo}**. Puede tardar un par de minutos en llegar." + botón vidrio **"Volver a iniciar sesión"**.

### 6.1b Inicio (después del login)

Flujo: **Login → Inicio → "Iniciar servicio" → Consola**. Desde el Inicio también se abren **Módulos, Servicios, Personas y Tiempos** (§6.6–6.10); esas pantallas usan la barra superior con el pill **"‹ Inicio"** y el título de la pantalla. El ViewModel del Inicio vive toda la sesión y **se recarga en silencio (sin spinner) cada vez que el Inicio vuelve a aparecer**, para reflejar lo que se cambió en otras pantallas (primero los módulos; luego tipos, personas y conteo de registros). Si el tipo de servicio elegido sigue existiendo se conserva; si se eliminó, se vuelve a sugerir uno.

- **Barra superior**: la misma de la consola, sin título de servicio ni botón de volver.
- **Saludo**: overline con degradado y fecha en mayúsculas ("DOMINGO, 4 DE OCTUBRE") · titular serif 44 "Buenos días, / Buenas tardes, / Buenas noches," + **nombre de la iglesia con degradado `accent`** · subtítulo "Todo listo: el TV está conectado." o "Conecta el TV antes de comenzar el servicio."
- **Tarjeta principal (hero)**, radio `xxl`, fondo `canvasElevated` + radial del color del servicio (abajo-izq.) + radial `glowViolet` (arriba-der.) + **IrisMark gigante (520) al 20 %** recortado en el borde derecho:
  - Izquierda: "● SERVICIO DE HOY" (con punto pulsante) o "PRÓXIMO SERVICIO" · nombre del servicio en serif 52 · meta "📅 Hoy · 10:00" y "⏱ 4 bloques · 1 h y 10 min" (o "Solo proyección") · **pills para elegir el tipo de servicio** (punto de color + nombre; seleccionado = cápsula blanca) · botón primario **"▶ Iniciar servicio"** (280).
  - Derecha (380): si el servicio tiene bloques (y el módulo de tiempo está activo) → "BLOQUES" + total, **línea de tiempo proporcional** (`BlockTimeline`: cápsulas con los colores del espectro) y lista (punto, nombre, responsable sugerido, "10 min"). Si no tiene → mini TV con el logo de la iglesia + "Este servicio no controla tiempos: solo proyección."
  - Sugerencia automática: el servicio programado hoy más próximo (hasta 2 h después de su inicio); si no, el primero.
  - **Sin tipos de servicio**: "Crea tu primer servicio" (serif 52) / "Configura tus tipos de servicio y, si quieres, sus bloques de tiempo." + botón primario **"Configurar servicios"** (280) → Servicios. A la derecha, el mini TV con el logo (sin la nota de "solo proyección").
- **Tarjetas (`IrisTile`)**: icono con tinte, título, subtítulo, contenido opcional y "›". **Al hacer clic navegan**: Servicios → §6.8 · Tiempos → §6.10 · Personas → §6.7 · Módulos → §6.6. Biblioteca aún no navega.
  - **Tiempos** (solo con módulo de tiempo): **solo el encabezado** — icono `timer` coral, "Tiempos" y "{N} servicios registrados" ("1 servicio registrado" / "Aún no hay registros"). Sin gráfico ni métricas: los datos están en la pantalla Tiempos.
  - **Servicios**: lista de tipos con punto de color y chip "⏱ Con tiempos" (verde) o "Solo proyección".
  - **Biblioteca**: contadores 2×2 (Letras, Música, Imágenes, Videos; los de multimedia se ocultan si el módulo está apagado).
  - **Personas** (solo con módulo de tiempo): avatares superpuestos con iniciales (colores del espectro) + "+3" y "{N} personas registradas".
  - **Módulos**: Letras, Biblia, Multimedia, Control de tiempo con punto verde "Activo" / gris "Apagado".
- Layout: fila 1 **Servicios · Biblioteca** (iguales); fila 2 **Tiempos · Personas · Módulos** (iguales). Sin control de tiempo: una sola fila **Servicios · Biblioteca · Módulos**. Las tarjetas de cada fila igualan su alto. Contenido máx. 1280 centrado (`IrisSize.contentWidth`).

**Modelos de la iglesia** (detalle en §9): `Person {id, name, initials}` · `ChurchModules {bible, multimedia, timeControl}` (Letras siempre activo) · `ServiceType {id, name, color, schedule?(weekday 1=dom, hour, minute), blocks: [BlockTemplate]}` · `BlockTemplate {id, name, plannedMinutes, defaultPersonId?}` · `ServiceRecord {id, date, serviceTypeId, blocks: [BlockRecord]}` · `BlockRecord {name, plannedSeconds, actualSeconds, personId?, personName?, status: completed|skipped|adjusted}`. **Exceso = real − previsto si > 0, sin margen de tolerancia.**

**Interfaces nuevas**: `IModuleSettingsRepository`, `IServiceTypeRepository`, `IPeopleRepository`, `ITimeRecordRepository` (mocks `MockModuleSettingsRepository`, `MockServiceTypeRepository`, `MockPeopleRepository`, `MockTimeRecordRepository`, todos sobre un único `InMemoryChurchStore` compartido; datos de ejemplo en `MockChurchData`). Métodos de escritura: `save(modules)`; `save(type)`/`delete(id)`; `add(name)`/`rename(id,to:)`/`delete(id)`; `save(record)`/`delete(id)`. `BlockRecord.personName` guarda el nombre al momento de registrar. Datos en §11 (3 tipos de servicio, 8 personas, 10 registros de los últimos domingos).

**Reglas de producto** (implementadas en §6.6–6.10 y §7.8–7.10): módulos por iglesia y lo apagado desaparece; control de tiempo opcional por tipo de servicio (se configura antes); un responsable por bloque; durante el servicio se puede agregar, omitir o editar bloques pendientes sin tocar la plantilla y al terminar se pregunta "Solo hoy / Guardar en la plantilla"; el reloj solo en la consola, nunca en el TV; solo se guardan tiempos, nunca el contenido usado; la lista de contenido del servicio siempre empieza vacía; Tiempos con lista por fecha y resúmenes filtrables (periodo, servicio, bloque, persona), solo dentro del app.

### 6.2 Consola en vivo (pantalla principal)

**Barra superior** (alto 68, padding 24):
Pill **"‹ Inicio"** · `IrisWordmark (24)` · separador vertical 1×28 · título del servicio (nombre del tipo, p. ej. "Culto general", `bodyEmphasized`) y debajo fecha "domingo, 4 de octubre · 10:00" (`caption`, `textTertiary`) · espacio · **reloj** grande (title3 rounded semibold, tabular, se actualiza cada minuto) · **estado del TV** (cápsula de vidrio: icono `tv` verde/ámbar + "Sala principal" / "1920 × 1080", o "Sin pantalla" / "Conecta un TV") · **avatar** (círculo 42 con degradado `accent` e iniciales, p. ej. "IV") → menú con el nombre de la iglesia y **"Cerrar sesión"** (destructivo). "‹ Inicio" vuelve al Inicio; si el cronómetro de bloques está en curso, pregunta antes (§6.9).

**Al iniciar un servicio desde el Inicio**, la lista **empieza vacía**, no hay nada en vivo y el fondo es el primero; la fecha bajo el título es la hora de inicio. El servicio de ejemplo de §11 ("Servicio dominical") solo se usa en vistas previas.

**Cuerpo: dos columnas** (padding 16, separación 16):

#### Columna 1 (ancho 340; 300 en el iPad mini, cuyo ancho horizontal es < 1150)

**a) Tarjeta "SERVICIO"** (`IrisSurface`, padding 16, ocupa todo el alto disponible)
- Encabezado: "SERVICIO" + contador de elementos + botón **"•••"** → menú con **"Vaciar servicio"** (destructivo, deshabilitado si está vacío).
- Lista de filas (`ServiceItemRow`):
  - Número con dos dígitos ("01", monoespaciado, `textTertiary`).
  - Icono del tipo en cuadrado 36 radio `sm` con fondo del tinte al 14 %.
  - Título (`calloutEmphasized`, 1 línea) + subtítulo (`caption`, `textTertiary`, 1 línea).
  - **Seleccionada**: fondo `surfaceRaised` radio `md` + barra vertical de 3 px con degradado `accent` a la izquierda. (No hay indicador de "en vivo" en la lista.)
- Interacciones:
  - **Clic/toque** → abre el elemento en el espacio de trabajo (nunca proyecta).
  - **Deslizar a la izquierda** → "Eliminar".
  - **Mantener presionado** → menú: "Duplicar", "Mover arriba", "Mover abajo", —, "Eliminar".
  - **Arrastrar** → reordenar.
- Vacío: "Servicio vacío" / "Toca Agregar para sumar letras, música, imágenes o videos." (sin Multimedia: "Toca Agregar para sumar letras.")
- Abajo: botón pill **"+ Agregar"** (ancho completo) → abre §6.4.
- Confirmación de vaciar: título "¿Vaciar el servicio?", botón destructivo "Quitar {N} elementos", mensaje "Esta acción no se puede deshacer."

**b) Tarjeta EN VIVO** (`IrisSurface`, padding 12)
- `LiveScreenView`: `ProjectionCanvas` del **frame actual del TV** (con el fondo elegido), borde `strokeStrong`, radio `md`, **badge "EN VIVO"** arriba a la izquierda. Nada más debajo.
- **Mini reproductor** (`NowPlayingView`) — aparece debajo **solo mientras suena música o video**, con transición desde abajo (nunca con el módulo Multimedia apagado):
  - Icono del tipo (música verde / video rosa) animado mientras suena + título + estado: "Sonando en el salón" (música) / "Reproduciendo en el TV" (video) / "En pausa".
  - Barra de avance arrastrable (tinte coral) + tiempos "0:52" y "-1:53" (monoespaciados).
  - Controles: ⏮ "Desde el inicio" · ⏯ "Pausar/Reproducir" (botón activo coral, un poco más grande) · ⏹ "Detener" · 🔁 "Repetir" (activo = coral).

#### Columna 2 — Espacio de trabajo (`IrisSurface`, padding 24)

Si el servicio controla tiempo, encima del espacio de trabajo va la **franja del cronómetro de bloques** (§6.9), separada 16.

**Encabezado**:
- Izquierda: `IrisChip` del tipo (icono + "Letra"/"Pasaje"/"Anuncio"/"Música"/"Imagen"/"Video" con su color) · título (`title`, serif) · subtítulo (autor / traducción / "Video · 2:45").
- Derecha (barra de herramientas, iconos redondos de vidrio):
  - Solo en modo Biblia: **‹** "Versículo anterior" y **›** "Versículo siguiente" (deshabilitados en los extremos; flechas ← → del teclado), y un separador.
  - **Borrador** (`eraser`) "Limpiar pantalla": conmuta; activo = coral. Deja en el TV solo el fondo.
  - **Fondo** (`photo.on.rectangle`) "Cambiar fondo": abre un **popover/flyout** con la cuadrícula de fondos (§6.5).
  - **Biblia** (`book.closed`): abre §6.3. Oculto si el módulo Biblia está apagado.
  - Pill **"Editar"** (lápiz) — sin acción aún; oculto en modo Biblia.

**Contenido según el tipo del elemento abierto**:

1. **Letra, Anuncio o Pasaje** → **cuadrícula de tarjetas** (columnas adaptativas de mín. 220, separación 16/24):
   - Cada tarjeta = `ProjectionCanvas` **sin fondo** (negra con letra blanca, siempre igual).
   - Debajo, **opcional**, el nombre de la sección ("Estrofa 1", "Versículo 16"); si la diapositiva no tiene nombre, no se muestra nada.
   - La que está en el TV: borde **`accent` 3 px** + sombra coral. Las demás: borde `stroke`. **Sin badges** "EN VIVO"/"SIGUIENTE".
   - **Clic** en una tarjeta → va al TV.
   - Al cambiar lo que está en vivo, la cuadrícula se desplaza para mantener visible esa tarjeta (útil en capítulos largos).
2. **Música, Video o Imagen** → **escenario de multimedia** (`MediaStageView`), centrado:
   - Tarjeta grande (máx. 640) con la vista previa; activa = borde `accent` 3 px + badge "REPRODUCIENDO" (música/video) o "EN PANTALLA" (imagen).
   - Botón primario de 300: "Reproducir" / "Reproduciendo" (música/video) · "Mostrar en el TV" / "En pantalla" (imagen). Deshabilitado si ya está activo.
   - Pista: Música → "La música suena en el salón; el TV no cambia. Contrólala desde el reproductor." · Video → "El video se muestra en el TV. Contrólalo desde el reproductor bajo la pantalla en vivo." · Imagen → "La imagen se muestra a pantalla completa en el TV."
   - **Un clic** en la tarjeta o en el botón presenta (sin doble clic).
3. Nada seleccionado → "Selecciona un elemento".

**Aviso de deshacer** (toast inferior centrado, cápsula de vidrio, alto 52): icono papelera + "Se quitó «{título}»" + **"Deshacer"** (color `ember`). Desaparece a los 5 s.

**Estados de carga**: spinner centrado; error → "No pudimos cargar el servicio." + botón "Reintentar".

### 6.3 Biblia (modal tamaño formulario, fondo `canvasElevated`)

Flujo en 3 pasos dentro del mismo modal:
- **Encabezado**: botón **‹ "Atrás"** (pasos 2–3) · título ("Biblia" → "{Libro}" → "{Libro} {capítulo}") · subtítulo "{instrucción} · Reina-Valera 1909" ("Elige un libro" / "Elige un capítulo" / "Elige un versículo") · botón cerrar.
- **Paso 1 — Libro**: **sin buscador**, solo selección. Segmentado **Antiguo Testamento | Nuevo Testamento** (por defecto Nuevo) · cuadrícula adaptativa (mín. 150) de celdas: nombre del libro + "{n} cap.".
- **Paso 2 — Capítulo**: cuadrícula adaptativa (mín. 64) de **solo números** (celdas de 56 de alto, número grande redondeado tabular).
- **Paso 3 — Versículo**: igual, solo números (spinner mientras carga el conteo).
- Al elegir el versículo: se cierra el modal, el **capítulo completo** se abre en el espacio de trabajo (una tarjeta por versículo, nombre "Versículo N", pie "Juan 3:16"), **ese versículo va al TV** y aparecen **‹ ›** para avanzar/retroceder.

### 6.4 Agregar al servicio (modal grande "page")

- Encabezado: "Agregar al servicio" / "Elige letras, música, imágenes o videos de tu biblioteca." + cerrar.
- Segmentado con **4 tabs: Letras · Música · Imágenes · Videos**. Sin el módulo Multimedia solo existe Letras: se oculta el segmentado y el subtítulo dice "Elige letras de tu biblioteca."
- Campo "Buscar" (placeholder "Título, autor o descripción"), filtra el tab actual sin acentos/mayúsculas.
- Contenido:
  - **Letras**: filas (`LibraryRow`): icono "Letra" (ámbar) · título · autor · **primer verso en serif cursiva** · checkmark.
  - **Música**: filas: icono música (verde) · título · descripción · **duración** (monoespaciada) · checkmark.
  - **Imágenes**: cuadrícula adaptativa (mín. 200) de miniaturas 16:9 + nombre + "JPG · 1920 × 1080"; checkmark arriba a la derecha.
  - **Videos**: igual, con ▶ al centro y **duración** en cápsula negra abajo a la derecha.
  - Seleccionado: borde `accent` (1.5 en filas, 3 en miniaturas) + checkmark encendido.
  - Sin resultados: vista de "búsqueda sin resultados".
- **Selección múltiple entre tabs**, conservando el orden en que se eligieron.
- Pie: "Seleccionados: {N}" (número con transición) + botón primario de 260 **"Agregar al servicio"** (deshabilitado con 0).
- Al confirmar: los elementos se **añaden al final** del servicio en orden de selección, se cierra el modal y se abre el primero agregado.

### 6.5 Selector de fondos (popover/flyout desde el icono "Cambiar fondo")

- Encabezado "FONDOS"; cuadrícula de 3 columnas fijas de 100.
- Cada muestra: degradado 92×52 radio 8, anillo blanco 2 px si está seleccionado, nombre debajo (`caption`).
- Al elegir: se aplica al TV y se cierra el popover.

### 6.6 Módulos

- Columna centrada máx. 720 (`IrisSize.settingsColumnWidth`) dentro de un `IrisSurface`.
- Título serif `headline` **"Módulos"** + "Elige qué partes de Iris usa tu iglesia. Lo que apagues desaparece de la consola."
- Una fila por módulo, separadas por divisores: icono tintado 40×40 (radio `sm`, fondo del tinte al 14 %), título `bodyEmphasized`, descripción `callout` `textSecondary` y `Toggle` (tinte `coral`):

| Módulo | Icono | Descripción | Toggle |
|---|---|---|---|
| Letras | `text.quote` (`ember`) | "Proyecta letras de canciones y anuncios. Siempre activo." | encendido y deshabilitado |
| Biblia | `book.closed.fill` (`violet`) | "Busca y proyecta versículos por libro, capítulo y versículo." | sí |
| Multimedia | `play.rectangle.fill` (`rose`) | "Música, imágenes y videos en la biblioteca y el reproductor." | sí |
| Control de tiempo | `timer` (`coral`) | "Mide los bloques de cada servicio y guarda sus tiempos." | sí |

- **Guardado inmediato** al cambiar, sin botón Guardar; los guardados se encadenan en orden. Si uno falla, el interruptor vuelve atrás y aparece un banner de error ("Algo salió mal. Inténtalo de nuevo.").
- Con Control de tiempo apagado, debajo de su fila: `IrisBanner(info)` "Los tiempos guardados se conservan. Puedes volver a activarlo cuando quieras."
- Efectos en el resto del app: §7.8.

### 6.7 Personas

Solo con el módulo de tiempo. Columna centrada máx. 720 dentro de un `IrisSurface`.
- Encabezado: **"Personas"** (serif) + "Quienes dirigen los bloques de tus servicios."
- **Agregar**: `IrisTextField` "Nueva persona" (icono `person.badge.plus`, placeholder "Nombre y apellido") + pill **"Agregar"** alineado con el centro del campo. Enter también agrega y deja el foco en el campo. Errores en el campo: "Escribe un nombre." / "Ya existe una persona con ese nombre." (comparación sin acentos, mayúsculas ni espacios al borde).
- **Lista** alfabética (español), separada por divisores: avatar 40 con iniciales (colores del espectro rotando), nombre y, a la derecha, "{N} bloques" / "1 bloque" (veces que dirigió un bloque en los registros; los omitidos no cuentan), `textTertiary`.
  - **Deslizar → "Eliminar"** → confirmación "¿Eliminar a {nombre}?" / "Sus tiempos guardados se conservan." → "Eliminar".
  - **Menú contextual**: "Renombrar" (alerta "Renombrar" con campo, "Cancelar" / "Guardar"; mismos errores, mostrados como banner) y "Eliminar".
- Vacío: "Aún no hay personas" / "Agrega a quienes dirigen la bienvenida, las alabanzas o la prédica."
- Eliminar a alguien **no borra registros** (guardan id + nombre) y lo quita como responsable sugerido de las plantillas.

### 6.8 Servicios (tipos de servicio y bloques)

**Lista**
- Encabezado: **"Servicios"** (serif) + "Crea los servicios de tu iglesia y, si quieres, sus bloques de tiempo." + botón primario **"+ Nuevo servicio"** (240) a la derecha.
- Cuadrícula adaptativa (mín. 320) de tarjetas `IrisSurface` (radio `xl`): franja de 4 px del color del servicio, nombre serif `title`, horario ("📅 Domingo · 10:00" o "Sin horario") y:
  - con bloques y módulo de tiempo activo → `BlockTimeline` + chip verde "⏱ 4 bloques · 1 h y 10 min";
  - si no → chip "Solo proyección".
- Clic en la tarjeta → editor. Vacío: "Aún no hay servicios" + primario **"Crear el primero"**.

**Editor** (hoja `page`, fondo `canvasElevated`, no se cierra deslizando). Trabaja sobre una **copia**; solo "Guardar" persiste.
- Encabezado: "Nuevo servicio" / "Editar servicio" · pill **"Cancelar"** · primario **"Guardar"** (180; deshabilitado si el nombre está vacío o hay errores; spinner mientras guarda).
1. **Nombre** — `IrisTextField` "Nombre" (icono `calendar`, placeholder "Ej. Culto general"). Duplicado → "Ya existe un servicio con ese nombre."
2. **COLOR** — 6 círculos de 36: ámbar, coral, rosa, violeta, índigo y verde; el elegido con anillo blanco de 2 px.
3. **HORARIO** — Toggle "Tiene horario fijo"; si está activo: 7 pastillas "Dom Lun Mar Mié Jue Vie Sáb" (elegida = cápsula blanca) y selector "Hora" (24 h).
4. **CONTROL DE TIEMPO** (solo con el módulo activo) — Toggle "Controlar el tiempo de este servicio" + "Divide el servicio en bloques con un tiempo previsto y un responsable." Al activarlo se crea un primer "Nuevo bloque" de 10 min.
   - Fila por bloque: asa de arrastre · nombre · minutos con − / + (1…240, "15 min") · menú de responsable ("Sin responsable", personas y "Agregar persona…" → alerta con campo; si el nombre ya existe se usa esa persona) · papelera.
   - Reordenar arrastrando · pill **"+ Agregar bloque"** ("Nuevo bloque", 10 min) · pie: `BlockTimeline` + "Total previsto: 1 h y 10 min".
   - Nombre de bloque vacío → borde `danger`; sin bloques → "Agrega al menos un bloque o desactiva el control de tiempo."
   - Apagar el control con bloques → confirmación "¿Quitar los bloques?" / "El servicio quedará solo para proyectar." → "Quitar bloques".
   - Con el módulo apagado la sección se oculta y los bloques existentes **no se tocan** al guardar.
5. **"Eliminar servicio"** (solo al editar, texto `danger`) → "¿Eliminar {nombre}?" / "Sus tiempos guardados se conservan." → "Eliminar".

### 6.9 Cronómetro de bloques (consola)

Solo si el módulo Control de tiempo está activo **y** el tipo de servicio tiene bloques. Franja encima del espacio de trabajo (`IrisSurface`, padding 16, radio `xl`):

- **Sin empezar**: "BLOQUES" + "4 bloques · 1 h y 10 min" + `BlockTimeline` + primario **"▶ Comenzar"** (200).
- **En curso**:
  ```
  ● PRÉDICA · Daniel Ruiz ▾
  18:42 / 40:00  [−21:18]                     [ Siguiente bloque → ]  [•••]  [ Terminar ]
  ─────────────────────────────────────────── (barra de progreso)
  Bienvenida ✓ 9:40 · Alabanzas ✓ 19:05 · Prédica ● · Anuncios
  ```
  - `IrisLiveDot` + nombre del bloque en `overline` + responsable como menú (permite corregirlo).
  - Reloj `title2` rounded semibold tabular: `textPrimary` (normal) · `warning` (por pasarse) · `danger` (pasado); "/ 40:00" en `textTertiary`; cápsula con el restante "−21:18" o el exceso "+3:10".
  - Barra de progreso de 4 px en `success` / `warning` / `danger`; al pasarse queda llena.
  - Migas: terminados con ✓ y su tiempo (`danger` si se pasaron), el actual con punto `live`, pendientes en `textTertiary`, omitidos tachados.
  - **"Siguiente bloque →"** (primario 220): abre el selector de responsable del siguiente (con el sugerido preseleccionado); al confirmar cierra el actual y abre el siguiente. En el último bloque dice **"Terminar"**.
  - **"•••"**: "Agregar bloque…", "Editar bloques pendientes…", "Omitir siguiente bloque".
  - **"Terminar"** (pill, siempre visible) → "¿Terminar el servicio?" / "Se guardarán los tiempos de los bloques." → "Terminar".
- **Terminado**: ✓ verde + "Servicio terminado · 1:26:10 (previsto 1:10:00 · +16:10)" (o "· a tiempo") + link **"Ver en Tiempos"**. Si falla el guardado: "No pudimos guardar los tiempos. Inténtalo de nuevo." + "Reintentar".

Hojas y diálogos (hojas `form`, fondo `canvasElevated`):
- **Selector de responsable**: "¿Quién dirige {bloque}?" + cerrar · lista de personas con `IrisCheckmark` (la sugerida primero con chip "Sugerido") · campo "Agregar persona" + "Agregar" (si el nombre ya existe, la selecciona) · link **"Sin responsable"** y primario **"Comenzar {bloque}"**.
- **Agregar bloque**: "Agregar bloque" + "Cancelar" · nombre ("Ej. Santa Cena") · "Minutos previstos" (1…240) · "Responsable" (menú) → **"Agregar"**. Se inserta después del bloque actual.
- **Bloques pendientes**: "Bloques pendientes" + "Listo" · lista reordenable con nombre, minutos, responsable y "Omitir" / "Restaurar" (omitidos tachados).
- **Al terminar con cambios** (bloques agregados, omitidos, editados o reordenados): alerta "Hoy hiciste cambios en los bloques" + resumen ("Agregaste Santa Cena y omitiste Anuncios.") → **"Solo hoy"** / **"Guardar en la plantilla"**. Después se guarda el registro.
- **"‹ Inicio" con el cronómetro en curso**: alerta "El servicio sigue en curso" → "Terminar y guardar" / "Salir sin guardar" (destructivo) / "Cancelar".

### 6.10 Tiempos

Solo con el módulo de tiempo. Encabezado **"Tiempos"** (serif) + segmentado **Registros | Resúmenes**. Sin registros: "Aún no hay tiempos" / "Se guardan al terminar un servicio con bloques."

**Registros** (dos columnas):
- **Izquierda (380)**: menú rápido por tipo ("Todos los servicios" + tipos) y lista agrupada por mes ("SEPTIEMBRE 2026", `IrisSectionHeader`). Fila: punto de color, servicio, fecha ("dom, 27 sept"), duración total tabular y chip "+16:10" (`danger`) o "A tiempo" (`success`). La seleccionada se marca como en la lista del servicio (fondo `surfaceRaised` + barra `accent`).
- **Derecha**: título serif del servicio + fecha larga ("domingo, 27 de septiembre de 2026") · métricas **Duración · Previsto · Exceso** (`title3` rounded; "A tiempo" en verde si no hubo exceso) · "BLOQUES": nombre + responsable, `IrisTimeBar` (real vs. previsto con escala común), real y "+4:05" / "a tiempo", chip "Ajustado"; los omitidos en `textTertiary` con "Omitido".
  - Menú "•••" por bloque: **"Ajustar duración…"** (hoja con ruedas de minutos y segundos → "Guardar"; el bloque queda "Ajustado") y **"Cambiar responsable"**.
  - Al final **"Eliminar registro"** → "¿Eliminar este registro?" / "Esta acción no se puede deshacer." → "Eliminar".

**Resúmenes**:
- **Filtros** (pills con menú): periodo — "Este mes", "Mes anterior", **"Últimos 3 meses"** (por defecto), "Este año", "Todo", "Elegir mes…" (hoja con ruedas de mes y año + "Ver este mes"); servicio ("Todos los servicios" + tipos); bloque ("Todos los bloques" + nombres encontrados); persona ("Todas las personas" + personas).
- **KPIs** (4 tarjetas `IrisSurface`): Servicios · Duración promedio · Exceso promedio por servicio · Bloques pasados ("12 de 20 · 60 %").
- **POR PERSONA** (tabla, mayor exceso total primero): avatar + nombre · Participaciones · Veces que se pasó · Exceso promedio (solo de las veces que se pasó) · Exceso máximo · Exceso total. Clic → hoja `page` con sus bloques del periodo (fecha, servicio, bloque, real / previsto, exceso) y un resumen neutro: "Se pasó en 5 de 8 bloques · promedio +6:20" (o "A tiempo en sus {N} bloques").
- **POR BLOQUE**: bloque + "Se pasó 3 de 8 veces" · Exceso promedio · `IrisTimeBar` del real promedio vs. previsto + "18:40 / 15:00".
- Sin datos con los filtros: "No hay tiempos con estos filtros".
- Lenguaje neutro, sin rankings llamativos. Solo se ve en el app (sin exportar ni compartir).

---

## 7. Reglas de comportamiento

### 7.1 "La lista abre, el espacio de trabajo presenta"
- Seleccionar en **Servicio** solo abre el elemento; **nunca** cambia el TV.
- Un **clic en una tarjeta / escenario** presenta.

### 7.2 Qué pasa al presentar según el contenido
| Contenido | TV | Reproducción |
|---|---|---|
| Texto (letra, anuncio, versículo) | Se muestra sobre el fondo elegido | Si hay **video** sonando, **se pausa**. La música sigue. |
| Imagen | A pantalla completa | Igual que texto |
| Video | Se muestra el video | Empieza (o continúa) la reproducción del video |
| Música | **No cambia** | Empieza la música (suena en el salón) |

- Presentar algo **desactiva "Limpiar pantalla"**.
- Volver a presentar el mismo medio que ya está cargado solo lo reanuda (no lo reinicia).
- Solo hay **una reproducción a la vez** (iniciar otra detiene la anterior).

### 7.3 Frame del TV (`liveFrame`)
- Si "Limpiar pantalla" está activo o no hay nada en vivo → **solo el fondo**.
- Si no → fondo + contenido de la diapositiva en vivo.

### 7.4 Reproducción
- Tic cada 0.5 s mientras suena (en la maqueta el tiempo es simulado).
- Al llegar al final: si "Repetir" → vuelve a 0; si no → se **detiene**.
- **Detener un video** que está en el TV deja el TV solo con el fondo.
- Arrastrar la barra mueve la posición.

### 7.5 Biblia
- El pasaje abierto es un elemento **temporal** (no se agrega a la lista del servicio).
- ‹ › mueven el versículo en vivo dentro del capítulo.

### 7.6 Edición del servicio
- **Eliminar**: si era el elemento abierto, se abre el siguiente (o el último); si estaba sonando, se detiene; si estaba en vivo, el TV queda solo con fondo. Muestra el **toast de deshacer** 5 s; "Deshacer" lo reinserta en su posición y lo abre.
- **Duplicar**: copia inmediatamente debajo y la abre.
- **Mover arriba/abajo**: intercambia con el vecino (deshabilitado en los extremos).
- **Reordenar arrastrando**: mueve los elementos antes del destino (o al final).
- **Vaciar**: requiere confirmación; detiene lo que sonaba del servicio y limpia el TV si mostraba algo del servicio.

### 7.7 Estado inicial de la demo
Solo en vistas previas (consola sin tipo de servicio): se abre el **2.º elemento** ("Sublime gracia") con su **2.ª estrofa en el TV** y el primer fondo ("Aurora") seleccionado. Un servicio iniciado desde el Inicio empieza con la lista vacía (§6.2).

### 7.8 Módulos
| Módulo apagado | Efecto |
|---|---|
| Biblia | Sin botón Biblia en la consola. |
| Multimedia | Agregar solo ofrece Letras (sin segmentado); los elementos de música, imagen y video no aparecen en el servicio; el mini reproductor nunca aparece; Biblioteca (Inicio) solo cuenta Letras. |
| Control de tiempo | Desaparecen las tarjetas Tiempos y Personas y sus pantallas; los servicios se ven "Solo proyección"; no hay cronómetro; el editor de servicios oculta los bloques sin borrarlos. Los tiempos guardados se conservan. |

- La consola recibe los módulos al iniciar el servicio; no cambian mientras dura.

### 7.9 Cronómetro de bloques
- Estado del reloj del bloque actual: `ratio = real / previsto` (segundos enteros) → **normal** < 0.9 · **por pasarse** ≥ 0.9 y ≤ 1 · **pasado** > 1. **Sin margen**: con 600 s previstos, 601 s ya es "pasado".
- El responsable se elige justo antes de que empiece cada bloque; el bloque anterior se cierra al confirmar.
- Los cambios del día (agregar, omitir, editar o reordenar pendientes) **no tocan la plantilla**. Solo se guardan en ella si al terminar se elige "Guardar en la plantilla": orden, nombres y minutos de hoy, sin los omitidos; los agregados toman el responsable de hoy como sugerido. Elegir otro responsable no cuenta como cambio.
- Terminar antes de tiempo: los bloques no alcanzados se guardan como **omitidos** (sin tiempo), pero no cuentan como cambio de plantilla.
- El registro guarda por bloque: nombre, previsto, real, responsable (id + nombre en ese momento) y estado (`completed` / `skipped`). **Solo tiempos**, nunca contenido. Fecha = inicio del primer bloque.
- Tic de 1 s solo mientras corre un bloque. El reloj **nunca** se muestra en el TV.

### 7.10 Tiempos y estadísticas
- Los bloques **omitidos no cuentan** en ningún cálculo; los **ajustados sí**.
- Exceso de un bloque = real − previsto si > 0 (sin margen). Exceso de un servicio = suma real − suma previsto de sus bloques contados, si > 0.
- Un servicio cuenta si tiene al menos un bloque que pasa los filtros. Duración promedio y exceso promedio son por servicio, sobre los bloques contados.
- Periodos por mes calendario: "Últimos 3 meses" = el mes actual y los dos anteriores.
- El filtro de bloque compara nombres sin acentos ni mayúsculas.
- Personas eliminadas: se muestra el nombre guardado en el registro; si no hay, "Persona eliminada". Sin responsable: "Sin responsable". Tipo eliminado: "Servicio eliminado".
- "Ajustar duración" marca el bloque como `adjusted`; "Cambiar responsable" actualiza el id y el nombre guardado.

---

## 8. Arquitectura

### 8.1 Estructura de carpetas (iPad → reflejar en Windows)
```
App/            AppDependencies (composition root), RootView, SessionStore, SignedInRoot (SignedInNavigator), IrisSplashView
Assets.xcassets AppIcon, IrisRing, IrisGlint, IrisLogo (§5.2)
Core/Models/    UserSession, ServicePlan, ProjectionFrame, Bible, Library, Church, ShowcaseItem
Core/Services/  Interfaces (Auth, ServicePlan, Bible, Library, MediaPlayback, ChurchRepositories…) + Mocks/
                (InMemoryChurchStore, MockChurchData, MockChurchRepositories, …)
DesignSystem/   Tokens/ (Color, Typography, Layout) y Components/ (Iris*, incl. IrisTile e IrisTimeBar)
Shared/         Projection/ProjectionCanvas · Formatting/ (IrisDurationFormat, IrisScheduleFormat, String+NameKey)
Features/
  Auth/         AuthViewModel, PasswordRecoveryViewModel, AuthValidator, Views/
  Home/         HomeViewModel, Views/ (HomeView, HomeHeroCard con BlockPlanView y BlockTimeline, HomeTiles)
  Modules/      ModulesViewModel, Views/
  People/       PeopleViewModel, Views/
  Services/     ServiceTypesViewModel, ServiceTypeEditorViewModel, Views/
  LiveConsole/  LiveConsoleViewModel, LiveConsoleScreen, BlockTimer, BlockTimerSheetModels, Views/ (… BlockTimerBar, BlockTimerSheets)
  Times/        TimesViewModel (+ DurationAdjustmentViewModel), TimeStatistics, Views/ (TimesView, TimeRecordDetailView, TimeSummaryView)
  Bible/        BiblePickerViewModel, BiblePickerView
  AddToService/ AddToServiceViewModel, AddToServiceView
irisTests/      Swift Testing: BlockTimer, TimeStatistics, ViewModels y repositorios mock
```

**WinUI 3 sugerido**
```
Iris.App/          App.xaml, MainWindow (+ capa del splash), ProjectionWindow (TV), Composition (DI), Assets/ (icono y SVG del logo)
Iris.Core/         Models/, Services/ (interfaces), Mocks/
Iris.DesignSystem/ Themes/Tokens.xaml (colores, brushes, tipografía, radios, espaciados), Controls/ (Iris*)
Iris.Features/     Auth/, Home/, Modules/, People/, Services/, LiveConsole/, Times/, Bible/, AddToService/ (Views + ViewModels)
Iris.Tests/        Pruebas de la lógica pura (BlockTimer, TimeStatistics) y de los ViewModels
```
- `Tokens.xaml`: `Color`, `SolidColorBrush`, `LinearGradientBrush` (accent), `FontFamily`, `x:Double` (tamaños) y `CornerRadius` como recursos. **Ninguna vista usa valores literales.**
- DI con `Microsoft.Extensions.DependencyInjection`; `AppDependencies.Mock` registra los mocks y **un solo** `InMemoryChurchStore` compartido por los cuatro repositorios de la iglesia.
- La lógica de cálculo vive en structs puros sin UI (`BlockTimer`, `TimeStatistics`) con el tiempo y el calendario inyectados, para poder probarla.

### 8.2 Navegación raíz
- Al abrir el app, el splash (§6.0) cubre la raíz y se quita solo; la raíz ya está cargada debajo.
- `SessionStore` (observable) mantiene `Session?`.
- Sin sesión → Acceso. Con sesión → `SignedInRoot` con `SignedInNavigator.Route`: `home`, `console(ServiceType)`, `modules`, `services`, `people`, `times`. Las pantallas secundarias llevan la barra superior con "‹ Inicio" y su título.
- El ViewModel del Inicio vive toda la sesión; el de la consola se crea al iniciar cada servicio y recibe el tipo de servicio, los módulos y las personas del Inicio.
- Cerrar sesión vuelve al acceso (se conserva el correo, se borra la contraseña).
- Transiciones: fundido.

### 8.3 ViewModels y sus intenciones
| ViewModel | Estado | Intenciones |
|---|---|---|
| `AuthViewModel` | `Mode` (signIn/signUp), campos, `FieldErrors`, `BannerMessage`, `IsSubmitting`, `FocusRequest`, `RecoveryViewModel?`, `ShowcaseItems`/`ShowcaseIndex` | `Submit`, `PresentPasswordRecovery`, `RunShowcase` (rota cada 5 s) |
| `PasswordRecoveryViewModel` | `Email`, `Phase` (editing/sending/sent), `Error` | `Send` |
| `HomeViewModel` | `Modules`, `ServiceTypes`, `People`, `RecordCount`, `Library`, `Display`, `SelectedServiceTypeId`, `NeedsServiceSetup` | `Appear` (carga la primera vez, luego `Refresh` silencioso), `SelectServiceType`, `StartSelectedService`, `OpenServices`, `OpenModules`, `OpenTimes`, `OpenPeople` |
| `ModulesViewModel` | `Modules`, `ErrorMessage`, `SaveTask` | `Load`, `SetModule(module, isOn)` (guardado inmediato y encadenado; revierte si falla) |
| `PeopleViewModel` | `People` (alfabético), `BlockCounts`, `NewName`, `NewNameError`, `RenameTarget`, `RenameDraft`, `PendingDeletion`, `ErrorMessage` | `Load`, `Add`, `BeginRename`, `ConfirmRename`, `RequestDelete`, `ConfirmDelete` |
| `ServiceTypesViewModel` | `ServiceTypes`, `Modules`, `Editor?` | `Load`, `CreateServiceType`, `Edit(type)` (el editor devuelve `Saved(type)` o `Deleted(id)` y la lista se actualiza sin recargar) |
| `ServiceTypeEditorViewModel` | Borrador (`Name`, `Color`, `HasSchedule`, `Weekday`, `Hour`, `Minute`, `TracksTime`, `Blocks`), `People`, `NameError`, `BlocksError`, `CanSave`, `IsSaving`, confirmaciones | `Load`, `SetTracksTime`, `ConfirmBlockRemoval`, `AddBlock`, `RemoveBlock`, `RenameBlock`, `SetMinutes`, `SetPerson`, `MoveBlocks`, `BeginAddPerson`, `ConfirmAddPerson`, `Save`, `RequestDelete`, `Delete` |
| `LiveConsoleViewModel` | `Phase`, `Service`, `Modules`, `People`, `Backgrounds`, `Display`, `SelectedItemId`, `Live` (itemId + slideIndex), `IsScreenCleared`, `SelectedBackgroundId`, `Playback?`, `RecentlyRemoved?`, `ScriptureItem?`, `IsPickingBackground`, `IsConfirmingClear`, `BiblePicker?`, `AddSheet?` · cronómetro: `BlockTimer?`, `Now`, `ResponsiblePicker?`, `AddBlockSheet?`, `IsEditingPendingBlocks`, `IsConfirmingFinish`, `IsAskingTemplateUpdate`, `IsConfirmingExit`, `FinishedRecord?`, `IsRecordSaved`, `RecordError` | `Load`, `SelectItem`, `GoLive(slideIndex)`, `PresentSelectedMedia`, `ToggleClearScreen`, `Next`, `Previous`, `SelectBackground`, `TogglePlayPause`, `RestartPlayback`, `ToggleLooping`, `Seek`, `StopPlayback`, `RunPlaybackClock`, `PresentAddToService`, `AppendToService`, `RemoveItem`, `UndoRemoval`, `DuplicateItem`, `MoveItem(by)`, `MoveItems(ids, before)`, `RequestClearService`, `ClearService`, `PresentBible`, `PresentScripture(book, chapter, verse)` · cronómetro: `StartBlocks`, `GoToNextBlock`, `SetLeader`, `PresentAddBlock`, `SkipNextBlock`, `PresentPendingEditor`, `RenamePendingBlock`, `SetPendingMinutes`, `SetPendingLeader`, `ToggleSkip`, `MovePendingBlocks`, `RequestFinish`, `FinishService`, `SaveRecord(updatingTemplate)`, `RetrySavingRecord`, `RequestExit(then)`, `FinishAndExit`, `ExitWithoutSaving`, `CancelExit`, `RunBlockClock` |
| `ResponsiblePickerViewModel` | `BlockName`, `SuggestedPersonId`, `People` (sugerida primero), `SelectedPersonId`, `NewName` | `Select`, `Confirm`, `ConfirmWithoutLeader`, `AddPerson` |
| `AddBlockViewModel` | `Name`, `Minutes`, `PersonId`, `People` | `Add` |
| `TimesViewModel` | `Records`, `ServiceTypes`, `People`, `Tab`, `RecordTypeFilter`, `SelectedRecordId`, `Adjustment?`, `SummaryFilter`, `PersonDetail?`, `PickedYear`, `PickedMonth` | `Load`, `SetRecordFilter`, `SelectRecord`, `BeginAdjustment`, `AdjustDuration`, `ChangeLeader`, `RequestDeleteSelected`, `DeleteSelected`, `SetPeriod`, `SetServiceFilter`, `SetBlockFilter`, `SetPersonFilter`, `BeginPickingMonth`, `ConfirmPickedMonth`, `ShowPersonDetail` |
| `BiblePickerViewModel` | `Step` (book/chapter/verse), `Books`, `Testament`, `SelectedBook`, `SelectedChapter`, `VerseCount` | `Load`, `SelectBook`, `SelectChapter`, `SelectVerse`, `Back` |
| `AddToServiceViewModel` | `Tabs` (permitidos por los módulos), `Tab`, `Query`, `Lyrics`, `Music`, `Images`, `Videos`, `Selection` (ordenada) | `Load`, `Toggle(selection)`, `Confirm` |

Derivados importantes de la consola: `LiveFrame`, `CardFrame(slide)` (sin fondo), `IsShowingScripture`, `IsShowingMedia`, `IsSelectedMediaActive`, `CanGoNext/Previous`, `LiveSlideId`, `AccountInitials` (iniciales de las 2 primeras palabras de > 2 letras del nombre de la iglesia), `ShowsBible`, `ShowsMultimedia`, `ShowsBlockTimer`, `PlannedBlocks`, `CurrentElapsed`, `CurrentClockState`, `CurrentProgress`, `CurrentDeltaText`, `Breadcrumbs`, `IsOnLastBlock`, `FinishedSummary`, `TemplateChangesMessage`.

---

## 9. Modelos de datos

```text
UserSession        { Id: Guid, ChurchName, LeaderName, Email }
SignInCredentials  { Email, Password }
SignUpRequest      { ChurchName, LeaderName, Email, Password }

ServicePlan        { Id, Title, Date, Items: [ServiceItem] }
ServiceItem        { Id, Kind, Title, Subtitle, Slides: [Slide] }
  Kind = Song (Letra) | Scripture (Pasaje) | Announcement (Anuncio) | Music | Image | Video
Slide              { Id, Label?: string, Content }
  Content = Text(body, footnote?) | Image(title, artwork[]) | Video(title, duration) | Audio(title, duration)

ProjectionBackground { Id, Name, Colors: [hex], IsAnimated }
ProjectionFrame      { Background?: ProjectionBackground (null = negro), Content }
  Content = Blank | Text(body, footnote?) | Image(title, artwork[]) | Video(title, duration) | Audio(title, duration) | Logo(name)
ExternalDisplay      { Name, Resolution }

BibleBook   { Id, Name, Testament (Old|New), ChapterCount }
BibleVerse  { Number, Text }

LyricSheet  { Id, Title, Author, Sections: [Slide], FirstLine (derivado) }
MediaAsset  { Id, Kind (Music|Image|Video), Title, Subtitle, Duration?, Artwork: [hex] }

Playback    { ItemId, Kind, Title, Duration (s), Elapsed (s), IsPlaying, IsLooping, Progress (derivado) }
RemovedItem { Item, Index }

Person          { Id, Name, Initials (derivado) }
ChurchModules   { Bible, Multimedia, TimeControl }              // Letras siempre activo
ServiceType     { Id, Name, Color (hex), Schedule?, Blocks: [BlockTemplate], TracksTime (derivado: Blocks no vacío) }
  Schedule = { Weekday (1 = dom … 7 = sáb), Hour, Minute }
  Palette  = ámbar #FFB547 · coral #FF7A59 · rosa #F0508C · violeta #9B5CFF · índigo #4E5BFF · verde #3DDC97
BlockTemplate   { Id, Name, PlannedMinutes, DefaultPersonId? }
ServiceRecord   { Id, Date, ServiceTypeId, Blocks: [BlockRecord], Planned/Actual/Overtime (derivados, sin omitidos) }
BlockRecord     { Id, Name, PlannedSeconds, ActualSeconds, PersonId?, PersonName? (copia al guardar),
                  Status: Completed | Skipped | Adjusted, Overtime/IsOver (derivados, sin margen) }

BlockTimer      (lógica pura del cronómetro; tiempo inyectado)
  Block   { Id (= id del bloque de la plantilla, o nuevo si se agregó hoy), Name, PlannedSeconds, PersonId?,
            StartedAt?, EndedAt?, IsSkipped, IsAddedToday }
  Phase   = NotStarted | Running | Finished
  ClockState = Normal | Warning | Over
  Start(personId, at) · Advance(nextPersonId, at) · Finish(at) · AddBlock(name, minutes, personId) · Skip / Restore
  UpdatePending(id, name?, minutes?, personId??) · MovePending(from, to) · SetPerson(id, personId)
  Elapsed(id, now) · ClockState(id, now) · TemplateChanges { Added, Skipped, Edited, IsReordered } · HasTemplateChanges
  Record(serviceTypeId, date, peopleNames) → ServiceRecord · UpdatedTemplate(original) → [BlockTemplate]

TimeStatistics  (lógica pura de los resúmenes; ahora y calendario inyectados)
  Filter  { Period, ServiceTypeId?, BlockName?, PersonId? }
  Period  = ThisMonth | LastMonth | Last3Months | ThisYear | All | Month(year, month)
  Entries · ServiceCount · AverageDuration · AverageOvertimePerService · OverBlocks (over, total)
  ByPerson [{ PersonId, Participations, TimesOver, AvgOvertimeWhenOver, MaxOvertime, TotalOvertime }]
  ByBlock  [{ Name, TimesOver, Total, AvgOvertimeWhenOver, AvgActual, AvgPlanned }]
```

Estilo visual por tipo (`ServiceItem.Kind`):

| Kind | Nombre visible | Icono (SF Symbol → Segoe Fluent Icons aprox.) | Color |
|---|---|---|---|
| Song | Letra | `text.quote` → *Quote* | `ember` |
| Scripture | Pasaje | `book.closed.fill` → *Library/Bookmarks* | `violet` |
| Announcement | Anuncio | `megaphone.fill` → *Megaphone* | `indigo` |
| Music | Música | `music.note` → *MusicNote* | `success` |
| Image | Imagen | `photo.fill` → *Photo* | `coral` |
| Video | Video | `play.rectangle.fill` → *Video* | `rose` |

Conversión de biblioteca → elemento del servicio:
- Letra → `Song`, subtítulo = autor, diapositivas = secciones.
- Música → `Music`, subtítulo "{descripción} · {duración}", 1 diapositiva `Audio`.
- Imagen → `Image`, subtítulo = descripción, 1 diapositiva `Image`.
- Video → `Video`, subtítulo "Video · {duración}", 1 diapositiva `Video`.
- Pasaje (Biblia) → `Scripture`, título "{Libro} {cap}", subtítulo = traducción, una diapositiva por versículo (label "Versículo N", pie "{Libro} {cap}:{N}").

---

## 10. Servicios (interfaces) y mocks

| Interfaz | Métodos | Mock |
|---|---|---|
| `IAuthService` | `SignIn(credentials) → Session`, `SignUp(request) → Session`, `RequestPasswordReset(email)` | Latencia 600 ms. `error@…` falla el login; `existe@…` falla el registro. |
| `IShowcaseContentProvider` | `Items() → [ShowcaseItem]` | 4 ejemplos (ver §11) |
| `IServicePlanRepository` | `CurrentService() → ServicePlan` | Latencia 450 ms |
| `IBackgroundRepository` | `Backgrounds() → [ProjectionBackground]` | 6 fondos |
| `IBibleRepository` | `TranslationName`, `Books()`, `VerseCount(bookId, chapter)`, `Verses(bookId, chapter)` | 66 libros reales; texto real RVR1909 solo en algunos capítulos |
| `ILibraryRepository` | `Lyrics()`, `Media(kind)` | Latencia 300 ms |
| `IMediaPlaybackService` | `Play(itemId)`, `Pause`, `Resume`, `Stop`, `Seek(s)`, `SetLooping(bool)` | No-op (el tiempo lo simula la consola). Real: `MediaPlayer` de Windows. |
| `IDisplayOutputService` | `ConnectedDisplay() → ExternalDisplay?`, `Present(frame)` | Devuelve "Sala principal · 1920 × 1080"; `Present` no hace nada. Real: actualiza la `ProjectionWindow`. |
| `IModuleSettingsRepository` | `Modules()`, `Save(modules)` | Los cuatro repositorios de la iglesia leen y escriben **un único `InMemoryChurchStore`** compartido (latencia 250 ms), así los cambios se ven en todas las pantallas |
| `IServiceTypeRepository` | `ServiceTypes()`, `Save(type)` (inserta o reemplaza por id), `Delete(id)` (los registros se conservan) | Ídem |
| `IPeopleRepository` | `People()`, `Add(name) → Person`, `Rename(id, name)`, `Delete(id)` (los registros conservan id + nombre; las plantillas quitan al responsable sugerido) | Ídem |
| `ITimeRecordRepository` | `Records()` (más reciente primero), `Save(record)` (inserta o reemplaza por id), `Delete(id)` | Ídem |

Los cuatro mocks de la iglesia son tipos separados porque sus `Delete(id)` reciben el mismo tipo de id (`Guid`).

---

## 11. Datos de prueba

**Sesión**: Iglesia Vida Nueva · Daniel Ruiz · pastor@vidanueva.org.

**Servicio "Servicio dominical"** (hoy 10:00):
1. **Bienvenida** (Anuncio) · "Anuncios de la semana" — "Bienvenidos a casa" (pie "Iglesia Vida Nueva"); "Cena congregacional\nSábado · 7:00 p. m." (pie "Salón principal"). *Sin labels.*
2. **Sublime gracia** (Letra) · John Newton — Estrofas 1–4:
   - "Sublime gracia del Señor / que a un pecador salvó; / fui ciego mas hoy veo yo, / perdido y Él me halló."
   - "Su gracia me enseñó a temer, / mis dudas ahuyentó; / ¡oh cuán precioso fue a mi ser / cuando Él me transformó!"
   - "En los peligros o aflicción / que yo he tenido aquí, / su gracia siempre me libró / y me guiará feliz."
   - "Y cuando en Sion por siglos mil / brillando esté cual sol, / yo cantaré por siempre allí / su amor que me salvó."
3. **Santo, santo, santo** (Letra) · Reginald Heber · trad. Juan B. Cabrera — Estrofas 1–2.
4. **Salmos 23:1-4** (Pasaje) · Reina-Valera 1909 — labels "v. 1"…"v. 4", pie "Salmos 23:N".
5. **Testimonios de bautismo** (Video) · "Video · 2:45".
6. **Castillo fuerte** (Letra) · Martín Lutero · trad. Juan B. Cabrera — 2 diapositivas *sin labels*.

**Fondos**: Aurora `#2A1658 #4E2A8C #131E5C` (animado) · Brasa `#3A1E08 #8C3A1E #3D1235` · Océano `#06283D #0E5E6F #0A1931` (animado) · Olivo `#0F2417 #2F5233 #111A12` · Alba `#5B2A3C #C0694E #2B1A3A` · Medianoche `#07070B #15151F #07070B`.

**Mini TV del login (rota cada 5 s)**: Sublime gracia · Estrofa 1 / Salmos 119:105 "Lámpara es a mis pies tu palabra, y lumbrera a mi camino." / ¡Santo, santo, santo! Señor omnipotente. / Mateo 11:28 "Venid a mí todos los que estáis trabajados y cargados, y yo os haré descansar."

**Biblia (RVR1909)**: índice completo de 66 libros con su número de capítulos (AT por defecto oculto; NT por defecto visible). Texto real en: **Salmos 23** (6 v.), **Juan 1:1-5** (de 51), **Juan 3:16-17** (de 36), **Génesis 1:1-3** (de 31). El resto: "Texto de ejemplo de {Libro} {cap}:{v}." y un conteo de versículos determinista (18–37).

**Biblioteca**
- Letras: Oh, qué amigo nos es Cristo (Joseph M. Scriven) · Roca de la eternidad (Augustus M. Toplady) · Cariñoso Salvador (Charles Wesley) · Sublime gracia (John Newton) · Santo, santo, santo (Reginald Heber) · Castillo fuerte (Martín Lutero).
- Música: Piano de fondo (6:12) · Preludio en Re (3:40) · Ofrenda · Guitarra acústica (4:05) · Pads de adoración en Sol (10:00) · Sublime gracia (instrumental) (4:32).
- Imágenes: Logo de la iglesia · Bienvenida · Santa Cena · Bautismos · Jóvenes · Misiones ("JPG · 1920 × 1080").
- Videos: Testimonios de bautismo (2:45) · Cuenta regresiva (5:00) · Anuncios de octubre (1:30) · Misión en Oaxaca (4:12, 4K) · Video de bienvenida (0:45).

**Iglesia** (`MockChurchData`)
- Módulos: los cuatro activos.
- Tipos de servicio: **Culto general** (dom 10:00, ámbar; Bienvenida 10 · Alabanzas 15 · Prédica 40 · Anuncios 5, con responsables sugeridos Carlos Pérez, Ana Torres, Daniel Ruiz y Lucía Gómez) · **Jóvenes** (sáb 19:00, rosa, sin bloques) · **ABC** (dom 9:00, violeta, sin bloques).
- Personas (8): Daniel Ruiz, Ana Torres, Carlos Pérez, Lucía Gómez, Marta Rivas, José Herrera, Sofía Méndez, Pablo Castro.
- Registros: los **últimos 10 domingos** del Culto general con responsables variados; uno con Anuncios omitido (hace 5 semanas) y uno con la Prédica ajustada (hace 6 semanas). La semana pasada: Bienvenida 9:40 · Alabanzas 19:05 · Prédica 51:30 · Anuncios 5:55.

Todos los textos de himnos y Biblia usados son de **dominio público**.

---

## 12. Adaptación a Windows 11

### 12.1 Ventana y layout
- `MainWindow` con **Mica** (Mica Alt) como fondo de la ventana y el `IrisBackground` estático encima; barra de título extendida (`ExtendsContentIntoTitleBar`) con la **barra superior de la consola** integrada en ella (respetando los botones del sistema a la derecha).
- Tamaño mínimo sugerido **1100 × 720**. Punto de quiebre: ancho ≥ 1400 → columna 1 de 340; menor → 300.
- Login: mismo layout de dos columnas.
- El iPad tiene un único diseño horizontal por pantalla (no hay diseños verticales que traducir). En Windows basta el tamaño mínimo de ventana; el lienzo escalado del iPad (principio 6) es una necesidad de iPadOS y no hace falta replicarlo.

### 12.2 Materiales
| iOS | Windows 11 |
|---|---|
| Liquid Glass (botones redondos, cápsulas, chips de estado, toast) | `AcrylicBrush` (in-app) con tinte `#0E0E15`, o `SystemControlAcrylicElementBrush` restyleado |
| `ultraThinMaterial` + `canvasElevated` 72 % (paneles) | `AcrylicBrush` (tinte `#0E0E15`, TintOpacity ~0.72) |
| Fondo de hojas/modales | `ContentDialog` o ventana modal con fondo `#0E0E15` |

### 12.3 Equivalencias de controles
| iPad | WinUI 3 |
|---|---|
| Hoja `.form` / `.page` | `ContentDialog` restyleado (ancho 620 / 900) o ventana modal propia |
| Popover (fondos) | `Flyout` anclado al botón |
| Menú (avatar, •••) | `MenuFlyout` |
| `confirmationDialog` | `ContentDialog` con botón primario destructivo |
| Context menu (mantener presionado) | **`ContextFlyout` con clic derecho** (+ mantener presionado en táctil) |
| Swipe actions | **Botón papelera que aparece al pasar el mouse** sobre la fila + `SwipeControl` en táctil + tecla **Supr** |
| Reordenar (`reorderable`) | `ListView` con `CanReorderItems="True"` + `AllowDrop` (o `ItemsRepeater` con drag & drop) |
| Grid adaptativo | `ItemsRepeater` + `UniformGridLayout` (MinItemWidth 220) o `GridView` |
| Slider | `Slider` restyleado (tinte coral) |
| ProgressView | `ProgressRing` |
| `ContentUnavailableView` | `StackPanel` centrado (icono + título + descripción) |
| SF Symbols | **Segoe Fluent Icons** (`FontIcon`) |

### 12.4 Teclado y mouse (añadidos de escritorio)
| Atajo | Acción |
|---|---|
| ← / → | Diapositiva / versículo anterior y siguiente (en Biblia; recomendable también en letras) |
| Espacio | Play/Pausa del mini reproductor |
| Esc | Cerrar modal / flyout |
| B | Limpiar pantalla (borrador) |
| Ctrl + B | Abrir Biblia |
| Ctrl + N | Agregar al servicio |
| Supr | Eliminar el elemento seleccionado del servicio (con toast de deshacer) |
| Ctrl + Z | Deshacer eliminación |
| Ctrl + D | Duplicar elemento |
| Alt + ↑ / ↓ | Mover elemento arriba / abajo |
| Enter | En formularios: siguiente campo / enviar |

- Estados **hover** en filas, tarjetas y botones (fondo `surfaceRaised`, cursor de mano en tarjetas).
- Tooltips en todos los botones de icono con su nombre ("Limpiar pantalla", "Cambiar fondo", "Biblia", …).

### 12.5 Sustituciones de texto
- "Tú diriges todo desde el iPad." → "Tú diriges todo desde tu computadora."
- "Toca…" → "Haz clic…" ("Haz clic en Agregar para sumar letras, música, imágenes o videos.", "Haz clic en Agregar para sumar letras.", "Haz clic para presentar").

### 12.6 Salida al TV (segundo monitor)
- `ProjectionWindow`: ventana **sin bordes, a pantalla completa** en el monitor secundario (`DisplayArea.FindAll()` → elegir el que no es el principal; `AppWindow.SetPresenter(FullScreen)`).
- Contenido: **solo `ProjectionCanvas`** con `LiveFrame` (sin cursor, sin controles), con la misma transición de fundido cruzado.
- El chip de estado de la barra superior muestra el nombre del monitor y su resolución; si no hay monitor secundario → "Sin pantalla / Conecta un TV".
- `IDisplayOutputService.Present(frame)` actualiza esa ventana.
- La reproducción real de video ocurre **en la ventana del TV** (`MediaPlayerElement`); la vista EN VIVO de la consola puede mostrar un fotograma o una copia de baja resolución.

### 12.7 Accesibilidad
- Todos los botones de icono con `AutomationProperties.Name`.
- Elementos seleccionados con `AutomationProperties.ItemStatus` / patrón de selección.
- Contraste: texto principal `#F6F3EE` sobre `#07070B`; texto blanco sobre fondos de proyección (los fondos llevan un velo negro del 20 %).
- Respetar "Mostrar animaciones" de Windows (desactivar pulsos, el giro del splash y transiciones largas).

### 12.8 Pantallas de la iglesia y cronómetro
- Módulos, Personas, Servicios y Tiempos: páginas del `Frame` raíz con la misma barra superior ("‹ Inicio" + título).
- Minutos de un bloque (− / +) → `NumberBox` con botones en línea (`SpinButtonPlacementMode="Inline"`, 1…240).
- Selector de hora del horario → `TimePicker` de 24 h. Ruedas de minutos/segundos ("Ajustar duración") y de mes/año ("Elegir mes…") → `NumberBox` o `ComboBox`.
- Tabla "POR PERSONA" → `ListView` con columnas de ancho fijo (o `DataGrid` del Community Toolkit); clic en una fila abre el detalle.
- Atajos sugeridos del cronómetro: **Ctrl + →** siguiente bloque · **Ctrl + Enter** terminar (con confirmación).

---

## 13. Inventario de textos

**Acceso**: PROYECCIÓN PARA IGLESIAS · Que cada palabra ilumine el templo. · Letras, versículos y multimedia en la pantalla grande. Tú diriges todo desde el iPad. · EN PANTALLA · Iniciar sesión · Crear cuenta · Te damos la bienvenida · Ingresa con el correo de tu iglesia para preparar el servicio. · Crea el espacio de tu iglesia · Configúralo en menos de un minuto y empieza a proyectar. · Correo de la iglesia · nombre@tuiglesia.org · Contraseña · Tu contraseña · ¿Olvidaste tu contraseña? · Entrar · Nombre de la iglesia · Iglesia Vida Nueva · Responsable · Nombre y apellido · Crea una contraseña · Mínimo 8 caracteres. · Al crear tu cuenta aceptas los Términos y la Política de privacidad. · Mostrar contraseña / Ocultar contraseña.

**Recuperación**: Recupera tu acceso · Escribe el correo de tu iglesia y te enviaremos un enlace para crear una nueva contraseña. · Enviar enlace · Revisa tu correo · Enviamos un enlace a {correo}. Puede tardar un par de minutos en llegar. · Volver a iniciar sesión · Cerrar.

**Consola**: Servicio dominical · Sala principal · 1920 × 1080 · Sin pantalla · Conecta un TV · Cuenta · Cerrar sesión · SERVICIO · Opciones del servicio · Vaciar servicio · ¿Vaciar el servicio? · Quitar {N} elementos · Esta acción no se puede deshacer. · Servicio vacío · Toca Agregar para sumar letras, música, imágenes o videos. · Toca Agregar para sumar letras. · Agregar · Duplicar · Mover arriba · Mover abajo · Eliminar · EN VIVO · Pantalla en vivo · Limpiar pantalla · Cambiar fondo · Biblia · Editar · Versículo anterior · Versículo siguiente · Selecciona un elemento · Envía esta diapositiva al TV · FONDOS · Se quitó «{título}» · Deshacer · No pudimos cargar el servicio. · Reintentar.

**Multimedia**: REPRODUCIENDO · EN PANTALLA · Reproducir · Reproduciendo · Mostrar en el TV · En pantalla · Toca para presentar · (pistas por tipo, §6.2) · Sonando en el salón · Reproduciendo en el TV · En pausa · Desde el inicio · Pausar · Detener · Repetir · Posición.

**Tipos**: Letra · Pasaje · Anuncio · Música · Imagen · Video.

**Biblia**: Biblia · Elige un libro · Elige un capítulo · Elige un versículo · Reina-Valera 1909 · Antiguo Testamento · Nuevo Testamento · {n} cap. · Atrás · Versículo {N}.

**Agregar**: Agregar al servicio · Elige letras, música, imágenes o videos de tu biblioteca. · Elige letras de tu biblioteca. · Letras · Música · Imágenes · Videos · Buscar · Título, autor o descripción · Seleccionados: {N}.

**Inicio**: {fecha} · Buenos días, / Buenas tardes, / Buenas noches, · Todo listo: el TV está conectado. · Conecta el TV antes de comenzar el servicio. · SERVICIO DE HOY · PRÓXIMO SERVICIO · Hoy · {hora} · {N} bloques · {total} · Solo proyección · Iniciar servicio · BLOQUES · Este servicio no controla tiempos: solo proyección. · Crea tu primer servicio · Configura tus tipos de servicio y, si quieres, sus bloques de tiempo. · Configurar servicios · Tiempos · {N} servicios registrados · 1 servicio registrado · Aún no hay registros · Servicios · {N} tipos de servicio · Con tiempos · Biblioteca · Tu contenido · Personas · Responsables de bloques · {N} personas registradas · Módulos · Elige qué usar · Activo · Apagado · Inicio.

**Módulos**: Módulos · Elige qué partes de Iris usa tu iglesia. Lo que apagues desaparece de la consola. · Letras · Proyecta letras de canciones y anuncios. Siempre activo. · Biblia · Busca y proyecta versículos por libro, capítulo y versículo. · Multimedia · Música, imágenes y videos en la biblioteca y el reproductor. · Control de tiempo · Mide los bloques de cada servicio y guarda sus tiempos. · Los tiempos guardados se conservan. Puedes volver a activarlo cuando quieras.

**Personas**: Personas · Quienes dirigen los bloques de tus servicios. · Nueva persona · Nombre y apellido · Agregar · Escribe un nombre. · Ya existe una persona con ese nombre. · {N} bloques · 1 bloque · Renombrar · Cancelar · Guardar · Eliminar · ¿Eliminar a {nombre}? · Sus tiempos guardados se conservan. · Aún no hay personas · Agrega a quienes dirigen la bienvenida, las alabanzas o la prédica.

**Servicios**: Servicios · Crea los servicios de tu iglesia y, si quieres, sus bloques de tiempo. · Nuevo servicio · {Día} · {hora} · Sin horario · {N} bloques · {total} · 1 bloque · {total} · Solo proyección · Aún no hay servicios · Crear el primero · Editar servicio · Cancelar · Guardar · Nombre · Ej. Culto general · Ya existe un servicio con ese nombre. · COLOR · Ámbar · Coral · Rosa · Violeta · Índigo · Verde · HORARIO · Tiene horario fijo · Dom · Lun · Mar · Mié · Jue · Vie · Sáb · Hora · CONTROL DE TIEMPO · Controlar el tiempo de este servicio · Divide el servicio en bloques con un tiempo previsto y un responsable. · Nuevo bloque · Nombre del bloque · {N} min · Minutos previstos · Responsable · Sin responsable · Agregar persona… · Agregar persona · Agregar bloque · Eliminar bloque · Total previsto: {total} · Agrega al menos un bloque o desactiva el control de tiempo. · ¿Quitar los bloques? · El servicio quedará solo para proyectar. · Quitar bloques · Eliminar servicio · ¿Eliminar {nombre}? · Editar servicio (pista de accesibilidad).

**Cronómetro**: BLOQUES · Comenzar · Siguiente bloque · Terminar · Opciones de bloques · Agregar bloque… · Editar bloques pendientes… · Omitir siguiente bloque · ¿Terminar el servicio? · Se guardarán los tiempos de los bloques. · Servicio terminado · {real} (previsto {previsto} · {exceso}) · a tiempo · Ver en Tiempos · No pudimos guardar los tiempos. Inténtalo de nuevo. · Reintentar · ¿Quién dirige {bloque}? · Sugerido · Sin responsable · Comenzar {bloque} · Agregar persona · Agregar bloque · Ej. Santa Cena · Minutos previstos · Bloques pendientes · Listo · Omitir · Restaurar · No quedan bloques pendientes · Hoy hiciste cambios en los bloques · Agregaste {bloques} · omitiste {bloques} · cambiaste {bloques} · cambiaste el orden · Solo hoy · Guardar en la plantilla · El servicio sigue en curso · Terminar y guardar · Salir sin guardar · Cancelar.

**Tiempos**: Tiempos · Registros · Resúmenes · Aún no hay tiempos · Se guardan al terminar un servicio con bloques. · Todos los servicios · Selecciona un registro · A tiempo · a tiempo · Duración · Previsto · Exceso · BLOQUES · Ajustado · Omitido · Opciones del bloque · Ajustar duración… · Ajustar duración · {bloque} · previsto {tiempo} · Minutos · Segundos · Cambiar responsable · Eliminar registro · ¿Eliminar este registro? · Esta acción no se puede deshacer. · Servicio eliminado · Persona eliminada · Este mes · Mes anterior · Últimos 3 meses · Este año · Todo · Elegir mes… · Elegir mes · Mes · Año · Ver este mes · Todos · Todas · Todos los bloques · Todas las personas · Servicios · Duración promedio · Exceso promedio por servicio · Bloques pasados · {n} de {total} · {porcentaje} · POR PERSONA · Persona · Participaciones · Veces que se pasó · Exceso promedio · Exceso máximo · Exceso total · Ver sus bloques · Se pasó en {n} de {total} bloques · promedio {exceso} · A tiempo en sus {N} bloques · POR BLOQUE · Se pasó {n} de {total} veces · No hay tiempos con estos filtros.

**Validación / errores**: Ingresa el correo de tu iglesia. · Ese correo no parece válido. · Ingresa tu contraseña. · Usa al menos 8 caracteres. · Escribe el nombre de tu iglesia. · Escribe el nombre del responsable. · El correo o la contraseña no coinciden. Revísalos e inténtalo de nuevo. · Ya existe una cuenta con ese correo. Intenta iniciar sesión. · No pudimos conectarnos. Verifica tu conexión a internet. · Algo salió mal. Inténtalo de nuevo. · No pudimos enviar el enlace. Inténtalo de nuevo.

Formato de fechas/horas siempre en **español** (`es`), independiente del idioma del sistema.

---

## 14. Checklist de paridad

- [ ] Diseño solo horizontal: un único diseño ancho por pantalla y mínimo de 1100 × 720.
- [ ] Tokens (colores, degradados, tipografía, espaciados, radios, sombras, movimiento) en un único diccionario de recursos.
- [ ] Componentes `Iris*` (§5) implementados y usados en todas las vistas.
- [ ] Logo con los archivos de `iris-logo-seleccionado`: icono del app (MSIX + `.ico`), `IrisMark` (anillo + brillo, estático) e `IrisWordmark` (§5.2).
- [ ] Splash al abrir: anillo que entra girando, brillo, fundido de salida; solo fundido sin animaciones (§6.0).
- [ ] `ProjectionCanvas` único para miniaturas, EN VIVO y ventana del TV.
- [ ] Inicio: saludo, hero "Iniciar servicio" con selector de tipo y plan de bloques (o "Crea tu primer servicio"), tarjetas Servicios/Biblioteca/Tiempos (compacta)/Personas/Módulos que navegan, recarga silenciosa al volver.
- [ ] Acceso: hero con mini TV rotativo, login libre, crear cuenta con validación, recuperación en 2 estados.
- [ ] Consola: barra superior (servicio, fecha, reloj, estado TV, cuenta).
- [ ] Columna 1: Servicio (seleccionar, eliminar con hover/Supr/deslizar, menú contextual, reordenar, vaciar con confirmación, estado vacío) + EN VIVO + mini reproductor.
- [ ] Columna 2: encabezado con chip/título/subtítulo y herramientas (‹ › solo Biblia, borrador, fondos, Biblia, Editar).
- [ ] Tarjetas negras con letra blanca, label opcional, solo resaltada la que está en el TV, autoscroll.
- [ ] Escenario de multimedia para Música / Video / Imagen con un solo clic.
- [ ] Reglas de presentación y reproducción (§7.2–7.4).
- [ ] Biblia en 3 pasos (libro por testamento, sin buscador → capítulo → versículo) y navegación ‹ ›.
- [ ] Agregar al servicio: 4 tabs (solo Letras sin Multimedia), búsqueda, selección múltiple ordenada, pie con contador.
- [ ] Selector de fondos en flyout.
- [ ] Toast de deshacer 5 s.
- [ ] Ventana de proyección a pantalla completa en el segundo monitor.
- [ ] Atajos de teclado, hover y tooltips (§12.4).
- [ ] Interfaces + mocks con los datos de §11; composition root `Mock` intercambiable; un solo `InMemoryChurchStore` para los repositorios de la iglesia.
- [ ] Módulos: interruptores con guardado inmediato; lo apagado desaparece del Inicio y de la consola (§7.8).
- [ ] Personas: agregar, renombrar y eliminar con validación sin acentos; contador de bloques; los registros conservan el nombre.
- [ ] Servicios: lista de tipos y editor (nombre, color, horario, bloques reordenables con responsable), validaciones y confirmaciones.
- [ ] Iniciar servicio: consola con lista vacía y el nombre del tipo en la barra.
- [ ] Cronómetro de bloques: 3 estados, colores por estado sin margen, selector de responsable, agregar/omitir/editar pendientes, pregunta de plantilla y salida con el servicio en curso (§7.9).
- [ ] Tiempos: registros por mes con detalle, ajuste de duración, cambio de responsable y eliminación; resúmenes con filtros, KPIs y tablas por persona y por bloque (§7.10).
- [ ] Pruebas de la lógica pura (`BlockTimer`, `TimeStatistics`) y de los ViewModels.
