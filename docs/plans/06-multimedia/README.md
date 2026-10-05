# 06 · Multimedia: caché de archivos y fondos

## Objetivo

Contrato §11. Imágenes, videos y música de la iglesia disponibles **sin conexión**: se descargan a una
caché local tras sincronizar, se muestran en "Agregar al servicio" y las imágenes marcadas como fondo
aparecen en el selector de fondos. Solo con el módulo Multimedia.

## Dependencias

Fases 02 y 04.

## Pasos

### 1. `MediaCache` (`Core/Media/`)
- Carpeta `LocalCacheFolder/Media/<churchId>/<mediaId>-<updatedAt ticks>.<ext>`.
- Tras cada sincronización descarga en segundo plano lo que falte o cambió: `GET /media/:id/download-url` y luego descarga con un
  `HttpClient` **sin** el `AuthHandler` (la URL ya va firmada), a un archivo temporal que se renombra al terminar. Máximo 2 a la
  vez; imágenes y música primero, videos después. Reanuda las incompletas al reiniciar.
- Borra los archivos de medios borrados o reemplazados. Estado por id: `NotDownloaded`, `Downloading(progress)`, `Ready(path)`, `Failed`.
- Si el disco libre baja de 1 GB, pausa los videos y avisa en el indicador de sincronización.

### 2. Biblioteca
- `LiveLibraryRepository.Media(kind)` desde la copia local; amplía el modelo de medio con `LocalPath`, `DownloadState`,
  `DurationSeconds`, `Width`, `Height`, `IsBackground`.
- "Agregar al servicio": miniaturas reales (imágenes decodificadas a tamaño reducido con `BitmapImage.DecodePixelWidth`; videos con
  `StorageFile.GetThumbnailAsync` o el primer cuadro), duración real. Sin descargar: progreso en la celda y no seleccionable
  ("Descargando…").

### 3. Fondos
- `ProjectionBackground` admite imagen además de degradado.
- `LiveBackgroundRepository`: los 6 degradados + las imágenes con `IsBackground` descargadas.
- `ProjectionCanvas`: fondo de imagen con `Stretch="UniformToFill"` y el velo negro del 20 %. Si la imagen en uso se borra, vuelve
  al primer degradado.
- Contenido `.image`: la imagen real a pantalla completa (`Uniform` sobre negro). Mantén el marcador en modo Mock.

### 4. API falsa
- `/media` (lista, detalle, `download-url`) y medios en `/sync/changes`.
- `download-url` devuelve `https://fake-storage.iris.local/<id>`, que el mismo `FakeIrisApiHandler` sirve (registra el handler
  también en el `HttpClient` de descargas en modo Fake).
- Archivos de muestra: genera en el momento 2 imágenes PNG de degradado (1920 × 1080) con `BitmapEncoder`, una marcada como fondo.
  Si en la PC hay `ffmpeg`, genera además un MP3 y un MP4 cortos (tono y barras de color) y guárdalos en `Assets/DevSamples/`
  (no van al paquete de Release); si no, deja audio y video sin muestra y anótalo en *Desviaciones*.

## Criterios de aceptación

- Modo Fake: las imágenes de muestra se descargan, aparecen con miniatura, se proyectan con "Simular sin conexión" activo, y la
  marcada como fondo aparece en el selector.
- Build limpio.

## Desviaciones

- En esta PC no hay `ffmpeg`, así que **no hay muestra de video**, y `Assets/DevSamples/` no se creó. La API falsa ofrece 2 imágenes (una marcada como fondo) y un audio de prueba; el audio es un tono **WAV** (`audio/wav`, tipo permitido por el contrato) y no un MP3. Para probar video hay que subir uno desde la web real o instalar `ffmpeg` y agregar la fila en `FakeSeed`.
- Las imágenes de muestra se generan con un codificador PNG propio en `Core/Networking/Fake/FakeSamples.cs` en lugar de `BitmapEncoder`, porque la API falsa vive en `Core` (sin dependencias de WinUI/WinRT). Son determinísticas: el tamaño anunciado coincide con los bytes que se sirven.
- La API falsa implementa también la subida (`POST /media/uploads`, `PUT` a `fake-storage.iris.local`, `POST /media`), `PATCH` y `DELETE`, y la descarga con `Range`, aunque la consola solo lee.
- Las descargas se reanudan con `Range` desde el archivo `.part` (tras un reinicio o un corte); si el servidor no admite `Range` se vuelve a empezar.
- Las miniaturas de video usan la miniatura del Explorador (`StorageFile.GetThumbnailAsync`); si no hay códec queda el degradado con la marca de reproducir. El reproductor real llega en la fase 09.
- La caché borra, además de los archivos viejos de la iglesia, las carpetas de otras iglesias.
- La capa visual (progreso de descarga en las celdas, miniaturas reales, fondo con imagen y velo del 20 %) no se pudo revisar con la app abierta en esta sesión.
