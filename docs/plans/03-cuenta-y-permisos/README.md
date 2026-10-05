# 03 · Cuenta y permisos

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

_(Completar al cerrar la fase.)_
