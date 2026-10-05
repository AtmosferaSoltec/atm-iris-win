# 04 · Iglesia: módulos, personas y tipos de servicio

## Objetivo

Que Módulos, Personas, Servicios e Inicio funcionen con datos reales de la copia local, escribiendo
por la cola. Contrato §6, §8, §9.

## Dependencias

Fases 02 y 03.

## Pasos

1. `LiveModuleSettingsRepository`: lee los módulos de la copia; guardar → local + `PUT /church/modules` en la cola.
2. `LivePeopleRepository`: lista desde la copia (orden alfabético en español con `CompareInfo` de `es`; `blockCount` del servidor);
   `AddAsync(name)` valida duplicado local con `NameKey`, crea con **id propio** (`Guid`) local + `POST /people { id, name }`;
   renombrar y borrar → local + `PATCH`/`DELETE`. Al borrar, aplica localmente la regla del contrato (quitar el responsable
   sugerido de las plantillas).
3. `LiveServiceTypeRepository`: lista desde la copia; guardar → local + `PUT /service-types/:id` (crea y edita); borrar → local +
   `DELETE`. Con el módulo de tiempo apagado, el editor reenvía los bloques existentes intactos.
4. `HomeViewModel`: sugerencia del servicio de hoy y saludo con `ChurchClock`. Recarga en silencio con `LocalStore.Changed`.
5. ViewModels de Módulos, Personas, Servicios y editor: sin cambios de interfaz; cambia el repositorio y se suscriben a `Changed`.
6. API falsa: `/church`, `/church/modules`, `/people`, `/service-types` con duplicados por *nameKey* y borrado suave.

## Criterios de aceptación

- Modo Fake: crear, renombrar y borrar personas y servicios; apagar un módulo y ver su efecto (spec §7.8) en Inicio y consola;
  repetir sin conexión y ver que se envía al reconectar.
- Build limpio.

## Desviaciones

_(Completar al cerrar la fase.)_
