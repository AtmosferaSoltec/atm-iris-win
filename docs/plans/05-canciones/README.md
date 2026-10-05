# 05 · Canciones

## Objetivo

Que "Agregar al servicio › Letras" y el contador de la Biblioteca del Inicio usen la biblioteca real
de la iglesia (contrato §10), desde la copia local. En la consola las canciones **solo se leen**: se
crean y editan en la web.

## Dependencias

Fase 02.

## Pasos

1. `LiveLibraryRepository.Lyrics()`: canciones de la copia local ordenadas por título (`NameKey`), mapeadas al modelo de letra
   existente (`sections` → diapositivas de texto con su `label`).
2. Búsqueda de `AddToServiceViewModel`: sigue siendo local; extiéndela al texto de las secciones (mismo criterio sin acentos).
3. Biblioteca vacía: "Aún no hay canciones" / "Agrégalas desde la web de Iris."
4. Una canción borrada en la web mientras está en el servicio en curso **no** se quita del servicio; desaparece de la biblioteca en
   la siguiente sincronización.
5. `copyright` (si existe): debajo del autor en la fila, en `caption` y `TextTertiary`.
6. API falsa: `/songs` (lista paginada con búsqueda sin acentos, detalle) y canciones en `/sync/changes`.

## Criterios de aceptación

- Modo Fake: las canciones de la semilla aparecen en "Agregar al servicio" y se proyectan; agregar una canción en el estado de la
  API falsa (desde el diálogo Conexión: botón de desarrollo "Agregar canción de prueba") aparece tras sincronizar.
- Build limpio.

## Desviaciones

- La consola no llama a `GET /songs`: la biblioteca sale siempre de la copia local (la búsqueda de "Agregar al servicio" es local y ahora incluye el texto de todas las secciones). Aun así la API falsa implementa `/songs` completo (lista paginada con búsqueda y relevancia, detalle, crear, reemplazar, borrar e importar) porque el contrato lo define y las pruebas lo ejercitan.
- Hasta la fase 06 `LiveLibraryRepository.MediaAsync` devuelve lo que haya en la copia local con miniaturas de relleno; la fase 06 lo completa con archivos reales.
- El botón "Agregar canción de prueba" del diálogo Conexión solo funciona en modo Fake con la sesión iniciada (necesita la iglesia activa); la canción aparece tras "Actualizar ahora".
