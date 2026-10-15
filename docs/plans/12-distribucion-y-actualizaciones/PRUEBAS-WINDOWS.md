# Pruebas en Windows del instalador y de la actualización

> **Para el agente de Windows.** La app ya está compilada y publicada por el CI, pero **nadie la ha ejecutado**: todo lo
> verificado hasta ahora fue desde una Mac (pruebas de `Core/`, compilación de C#, `x:Bind`). Tu trabajo es probarla de
> verdad, corregir lo que falle y dejar el resultado anotado al final de este archivo. El plan y las decisiones están en
> [`README.md`](README.md); no las cambies sin avisar.

## Reglas

- **No subas a `production`** ni cambies `VERSION` sin que el dueño lo apruebe: `production` publica el instalador a los clientes.
  Trabaja en `develop` y avisa.
- **No escribas en la base de producción** (`app_versions`) ni subas archivos al VPS a mano. Para probar la actualización usa
  el escenario local de la prueba 6.
- **No guardes contraseñas ni claves en el repo, en commits ni en este archivo.** El dueño te da una cuenta de pruebas en la
  sesión; úsala solo ahí.
- Si algo falla, **arréglalo en el código**, no lo esquives. Si el arreglo cambia el comportamiento acordado, pregunta antes.

## Qué ya está publicado (para referencia)

| Qué | Dónde |
|---|---|
| Instalador (1.0.0, sin firmar, 77 MB) | https://iris.atmosferast.com/downloads/windows/Iris-Setup.exe |
| Feed de actualizaciones | https://iris.atmosferast.com/downloads/windows/releases.win.json |
| Página de descarga | https://iris.atmosferast.com/downloads |
| Versión que exige la base | `GET https://iris-api.atmosferast.com/api/v1/app-version?platform=windows&version=X` |

La app **no está firmada**: Windows mostrará "Windows protegió tu PC". Es lo esperado (decisión tomada; ver README §8).

## Accesos

El dueño crea una **cuenta de pruebas** en https://iris.atmosferast.com/login (iglesia "Iglesia de Pruebas") y te da correo y
contraseña por el chat. No uses la cuenta personal del dueño. Para la prueba local de actualización (6) hace falta la API en
tu PC: `../atm-iris-api` → `pnpm start:dev` (necesita su `.env` y PostgreSQL local; si no puedes levantarla, di cuál
prueba quedó sin hacer y por qué).

## Pruebas (en este orden)

Marca cada una en la tabla de **Resultados**. En cada fallo anota qué viste y el mensaje del log
(`%LOCALAPPDATA%\Iris\logs`).

### 1. Compila y pasa las pruebas
`dotnet build Iris.csproj -p:Platform=x64` sin errores y `dotnet test Tests/Iris.Tests.csproj` en verde. Abre la app con
F5 (perfil *Iris (Unpackaged)*, modo **Fake**): login, Home, consola. Esto cubre el XAML que nunca se compiló
(`UpdateGateSheet`, la bienvenida de Home, el login sin "Crear cuenta").

### 2. Instalador, en una PC limpia
Sin .NET ni Windows App SDK instalados (una VM de Windows 10/11 recién creada sirve). Descarga el instalador desde
`/downloads`, ejecútalo (acepta el aviso de SmartScreen).
- Se instala sin pedir administrador y la app abre sola.
- Aparece en el menú Inicio y en "Aplicaciones instaladas" con el nombre Iris.
- **Si no abre o se cierra al instante**: casi seguro es el recorte (`PublishTrimmed`). Mira el log y
  `Event Viewer → Windows Logs → Application`. Corrige excluyendo lo que el recorte quite
  (`<TrimmerRootAssembly Include="..." />`) y vuelve a generar con `Scripts\Release-Windows.ps1 -SkipDownload`.

### 3. Primer uso
- Muestra la pantalla de inicio de sesión **sin** "Crear cuenta"; el enlace "Crea tu cuenta en la web" abre el navegador en
  https://iris.atmosferast.com.
- Inicia sesión con la cuenta de pruebas → aparece la **bienvenida** (una sola vez). "Abrir la web de Iris" abre el navegador.
- Cierra la app y ábrela: sigue con la sesión y **no** vuelve a mostrar la bienvenida.
- Cerrar sesión y volver a entrar: la bienvenida no se repite (es por persona).

### 4. Uso real mínimo
Con la cuenta de pruebas (crea antes en la web una letra y sube una imagen):
- La letra y la imagen aparecen en la app tras sincronizar.
- Segundo monitor/TV: proyecta la letra (la prueba de dos monitores está pendiente desde la fase 11).
- Iniciar un servicio y terminarlo: se guarda el registro en Tiempos.
- Sin internet (desconecta la red): la app abre, deja trabajar con lo ya sincronizado y **no** muestra ningún aviso de versión.

### 5. Desinstalar
Desde "Aplicaciones instaladas": la app se va y `%LOCALAPPDATA%\Iris` (datos, sesión, copia local) **permanece**. Al
reinstalar, la sesión sigue.

### 6. Actualización obligatoria (escenario local, sin tocar producción)
Objetivo: instalar una versión "vieja" y que la app se actualice sola a una "nueva".

1. `Scripts\Release-Windows.ps1 -Version 1.0.0 -SkipDownload` → instala `Releases\Iris-Setup.exe` en la PC.
2. Cambia `VERSION` a `1.0.1` **solo en tu copia local** (no lo subas) y corre
   `Scripts\Release-Windows.ps1 -Version 1.0.1 -SkipDownload`. `Releases\` ahora tiene ambas versiones.
3. Sirve la carpeta: `cd Releases; python -m http.server 8099`.
4. API local: en `app_versions` pon `windows` en `1.0.1`
   (`pnpm db:set-app-version windows 1.0.1`, en `atm-iris-api` con su base **local**).
5. Abre la app instalada con la variable `IRIS_FEED_URL=http://localhost:8099/` y apunta la app a tu API local
   (Ctrl+Shift+F12 → Conexión → modo Live, dirección `http://localhost:3020/api/v1` o el puerto que uses).
6. Esperado: aparece **"Hay una nueva versión de Iris"**; **Esc y clic fuera no lo cierran**; **Actualizar** muestra el
   progreso, descarga, la app se reinicia y queda en 1.0.1 **con la sesión intacta**.
7. Variantes: (a) con la red cortada no aparece nada; (b) con un servicio en curso no aparece, y sí al volver a Inicio;
   (c) con la base en `1.0.1` pero el feed solo con `1.0.0` la app abre normal (no se bloquea).
8. Deja la base local como estaba (`pnpm db:set-app-version windows 1.0.0`).

## Qué hacer con lo que encuentres

1. Arregla en `develop` con commits pequeños y `Scripts/MacCheck`/`dotnet test` en verde.
2. Anota en **Resultados** qué falló y cómo quedó.
3. Si hay cambios, **avisa al dueño** para que él decida subir a `production` (eso publica un instalador nuevo, p. ej.
   1.0.1, con `VERSION` subido y, después, `app_versions`).

## Resultados

| # | Prueba | Resultado | Notas |
|---|---|---|---|
| 1 | Compila y pruebas | | |
| 2 | Instalador en PC limpia | | |
| 3 | Primer uso | | |
| 4 | Uso real mínimo | | |
| 5 | Desinstalar | | |
| 6 | Actualización obligatoria | | |
