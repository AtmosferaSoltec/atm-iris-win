# Verificación desde una Mac

WinUI 3 no compila ni corre en macOS. Esta carpeta permite revisar casi todo desde una Mac antes de llevar los cambios a
Windows:

| Paso | Qué revisa | Qué **no** revisa |
|---|---|---|
| 1. Pruebas de `Core/` | La lógica sin interfaz (`Tests/Iris.Tests.csproj`): API falsa, sincronización, caché, repositorios. | — |
| 2. `WinCheck` | Todo el C# de la app (ViewModels, `Shell/`, código detrás de las vistas) contra las referencias reales de Windows App SDK. `xamlgen.py` escribe lo que generaría el compilador de XAML (campos `x:Name`, `InitializeComponent`, `Bindings`). | El XAML en sí: estructura, estilos, plantillas. |
| 3. `BindCheck` | Cada ruta `{x:Bind …}` contra los tipos compilados (propiedades, métodos, funciones estáticas) y que cada `{StaticResource}` exista en algún diccionario. | `Setter Target`, conversiones de tipo, lo que solo falla al ejecutar (`XamlParseException`). |

Lo que queda (compilar el XAML, abrir la app, probar con dos monitores) se hace en Windows:
`dotnet build Iris.csproj -p:Platform=x64` y la lista de `docs/plans/11-paridad-ipad-y-escritorio`.

## Uso

```sh
# .NET 10 SDK (en una carpeta propia, sin tocar el sistema):
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet-iris
bash /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir ~/.dotnet-iris   # para correr las pruebas (net8.0)

DOTNET=~/.dotnet-iris/dotnet Scripts/MacCheck/mac-check.sh
```

Si macOS mata el ejecutable `dotnet` recién instalado (código 137), cópialo sobre sí mismo para renovar su firma en caché:
`cp ~/.dotnet-iris/dotnet /tmp/d && mv /tmp/d ~/.dotnet-iris/dotnet`.

`Iris.csproj` excluye `Scripts/**`: nada de esta carpeta entra a la app.
