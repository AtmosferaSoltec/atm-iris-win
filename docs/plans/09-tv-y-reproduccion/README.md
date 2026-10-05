# 09 · TV y reproducción reales

## Objetivo

Completar lo que la maqueta simulaba (spec §7.2–7.4 y §12.6): el segundo monitor se detecta en
caliente y la música y el video se reproducen de verdad.

## Dependencias

Fases 06 (archivos locales) y 07. La lógica de presentación de `LiveConsoleViewModel` **no cambia**: solo las
implementaciones de `IDisplayOutputService` y `IMediaPlaybackService`.

## Pasos

### 1. Segundo monitor
- `ProjectionDisplayService` hoy busca el monitor una sola vez (`_probed`). Cámbialo para reaccionar a conexiones y desconexiones
  en caliente: vigila los monitores (`DisplayArea.FindAll()` al recibir cambios de pantalla — investiga el evento adecuado del
  Windows App SDK 2.5, por ejemplo un `DisplayAreaWatcher`, o `DisplayMonitor`/`DeviceWatcher` de WinRT como alternativa).
  - Al conectar: crea la `ProjectionWindow` en el monitor secundario, sin bordes y a pantalla completa, y presenta el último frame.
  - Al desconectar: cierra la ventana sin afectar la consola.
  - Evento `DisplayChanged` para que el chip de `AppTopBar` pase entre "{nombre del monitor} · 1920 × 1080" y "Sin pantalla ·
    Conecta un TV" en vivo. Nombre real del monitor si se puede obtener; si no, "Pantalla 2".
- Opción en el menú del chip de TV: "Elegir pantalla" cuando hay más de un monitor secundario.
- El cursor del mouse no se muestra sobre la ventana del TV.

### 2. Reproducción
- `LiveMediaPlaybackService` con `Windows.Media.Playback.MediaPlayer`:
  - Música: reproduce el archivo local en el salón; el TV no cambia.
  - Video: un `MediaPlayer` cuyo `MediaPlayerElement` vive en la `ProjectionWindow` (dentro del `ProjectionCanvas`); la vista EN VIVO
    de la consola muestra el mismo reproductor reducido (otro `MediaPlayerElement` con `SetMediaPlayer` del mismo `MediaPlayer`, o un
    cuadro cada segundo si no se puede compartir; investiga y elige la opción estable).
  - `Play`, `Pause`, `Resume`, `Stop`, `Seek`, `SetLooping` reales; al terminar, repetir o detener (spec §7.4).
  - Publica tiempo y duración reales (evento `PlaybackSession.PositionChanged`, en el hilo de interfaz) para que el mini reproductor deje
    de simularlos: adapta `LiveConsoleViewModel.RunPlaybackClock` para leer del servicio en modo Fake/Live.
  - Una sola reproducción a la vez; presentar texto o imagen pausa un video en curso; la música sigue.
  - Controles multimedia del sistema (`SystemMediaTransportControls`): título y play/pausa.
- Atajo `Espacio` ya definido en la spec: verifica que controla la reproducción real.

## Criterios de aceptación

- Con dos monitores: conectar y desconectar en caliente funciona sin reiniciar; letras, imágenes y videos se ven en el TV sin interfaz;
  la música suena y el mini reproductor muestra el avance real (con las muestras de la fase 06 si existen).
- Build limpio.

## Desviaciones

_(Completar al cerrar la fase.)_
