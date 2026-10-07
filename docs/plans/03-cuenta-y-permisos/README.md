# 03 · Cuenta y permisos

> **Reemplazada por el contrato v1 (ver fase 11).** La API ya no tiene roles, equipo ni selector de iglesia: una cuenta por
> iglesia, todas las acciones disponibles. Lo de abajo describe lo que se construyó entonces; hoy `Mapping.ToSession` da
> todos los permisos y ninguna otra iglesia, por lo que nada de esto se activa. Queda el menú de cuenta (nombre, correo,
> iglesia, cerrar sesión, cerrar en todos).

## Objetivo

Que la consola respete los roles (contrato §3), permita cambiar de iglesia y cerrar sesión en todos
los dispositivos. La gestión del equipo (invitar, roles) **se hace en la web**; Windows no la replica.

## Dependencias

Fases 01 y 02.

## Pasos

### 1. Menú de la cuenta (avatar de `AppTopBar`, `MenuFlyout`)
- Encabezado: nombre, correo, iglesia actual y rol ("Dueño", "Administrador", "Operador").
- "Cambiar de iglesia" (si hay más de una): submenú; elegir otra → confirmación de cola pendiente (fase 02) → `SwitchChurchAsync`
  → limpiar copia local → primera sincronización → Inicio.
- "Cerrar sesión" y "Cerrar sesión en todos los dispositivos" (destructivo; `ContentDialog` "Se cerrará la sesión en la web, el
  iPad y Windows. Tendrás que volver a iniciar sesión en cada uno.").
- Pie: "El equipo se administra desde la web."
- API falsa: `switch-church`, `sign-out-all`, `me`.

### 2. Permisos en las pantallas
Usa `session.Can(...)`. Sin el permiso la acción **no aparece** (salvo donde se indica):

| Pantalla | Permiso | Sin permiso |
|---|---|---|
| Módulos | `modules.manage` | Interruptores deshabilitados + nota "Solo un administrador puede cambiar los módulos." |
| Servicios (lista y editor) | `serviceTypes.manage` | Sin "Nuevo servicio"; el editor se abre en solo lectura |
| Personas | `people.manage` | (los tres roles lo tienen) |
| Consola · "Guardar en la plantilla" | `serviceTypes.manage` | La pregunta de cambios solo ofrece "Solo hoy" |
| Tiempos · ajustar, cambiar responsable, eliminar | `records.manage` | Sin menú "•••" ni "Eliminar registro" |
| Consola · guardar tiempos al terminar | `records.write` | (los tres roles lo tienen) |

- Un `403` en la cola (rol cambiado desde la web): regla de la fase 02 (descartar y avisar) y `GET /auth/me` para actualizar permisos.
- `GET /auth/me` en cada sincronización disparada al volver al Inicio.

## Criterios de aceptación

- Modo Fake: con `operador@vidanueva.org` no aparecen las acciones de administración.
- Build limpio.

## Desviaciones

- Personas: los tres roles tienen `people.manage`, así que no hay vistas distintas; los comandos de agregar, renombrar y eliminar usan `CanExecute` con ese permiso (quedarían deshabilitados, no ocultos, si algún rol futuro no lo tuviera).
- Cambiar de iglesia o cerrar sesión en todos los dispositivos sin conexión muestra un mensaje ("No pudimos conectarnos…") en vez de cerrar solo lo local, porque ambas acciones necesitan al servidor. "Cerrar sesión" sí funciona sin conexión.
- "Cerrar sesión en todos los dispositivos" también pide confirmar si hay cambios sin enviar (además del diálogo propio del plan).
- El editor de servicios de solo lectura deshabilita el formulario completo (queda atenuado) y cambia "Cancelar" por "Cerrar"; no hay un modo de lectura con diseño aparte.
- Con permiso `records.write` ausente (ningún rol actual) la consola no guarda los tiempos y avisa "No tienes permiso para guardar los tiempos.".
- Los glifos del menú de cuenta y su aspecto no se pudieron revisar visualmente en esta sesión.
