# Plataforma Iris · decisiones comunes

> Documento **idéntico** en los cuatro repos (`docs/plans/00-fundamentos/plataforma.md`).
> Explica qué se construye en la v1, cómo encajan las piezas y cómo trabajan los cuatro
> agentes a la vez. Lo específico de cada repo está en su propio `docs/plans/README.md`.

---

## 1. Producto

**Iris** proyecta en el TV de la iglesia letras de canciones, versículos, imágenes y videos, y controla el tiempo de cada bloque del servicio.

| Pieza | Repo | Dónde corre | Para qué |
|---|---|---|---|
| **API** | `atm-iris-api` | Mac (local) | Cuentas, iglesias, contenido, archivos, Biblia, tiempos y sincronización |
| **Web** | `atm-iris-web` | Mac (local) | Administración: canciones, multimedia, servicios, personas, equipo, tiempos. **No proyecta** |
| **iPad** | `atm-iris-ios` | Mac (Xcode, simulador) | Consola en vivo: proyecta al TV, reproduce música y video, mide los tiempos |
| **Windows** | `atm-iris-win` | **PC con Windows** | La misma consola, para una PC con un segundo monitor o proyector |

El diseño, los textos y las reglas de pantalla están en `IRIS_SPEC.md` (en `atm-iris-ios/` y copiado en `atm-iris-win/docs/`). El contrato de la API, en `docs/contract/api-v1.md` (API) o `docs/api-contract.md` (clientes).

### 1.1 Alcance de la v1

| Incluido | Fuera de la v1 (se deja preparado, no se construye) |
|---|---|
| Crear cuenta, iniciar sesión una vez por dispositivo, recuperar contraseña con código | Facturación, planes y pagos |
| Varias iglesias por usuario, cambio de iglesia | Planificar el orden del servicio con anticipación (la lista de la consola **empieza vacía**, regla del producto) |
| Equipo: invitar por correo con roles `owner`, `admin`, `operator` | Tiempo real entre dispositivos (websockets): se sincroniza por intervalos |
| Módulos, personas, tipos de servicio con bloques | Varias traducciones de la Biblia (el modelo lo admite; solo se carga RVR1909) |
| Biblioteca de canciones con búsqueda e importación `.txt` | Edición de imágenes o recorte de video |
| Multimedia: imágenes, videos y música en almacenamiento S3 compatible, con fondos personalizados | Auditoría de cambios con pantalla propia |
| Biblia RVR1909 completa, sin conexión en las consolas | Despliegue a producción (todo queda **en local** por ahora) |
| Consolas **sin conexión**: copia local + sincronización + cola de escrituras | App de iPhone o Android |
| Salida real al TV (pantalla externa en iPad, segundo monitor en Windows) y reproducción real de audio y video | |
| Registros de tiempos y resúmenes en las consolas y en la web | |

---

## 2. Arquitectura

```
                  ┌──────────────────────────────┐
  Navegador ────▶ │  atm-iris-web (Next.js 16)   │  Server Components + Server Actions (BFF)
                  │  tokens en cookie cifrada     │──────┐
                  └──────────────────────────────┘      │  HTTPS JSON, Bearer
                                                         ▼
  iPad (SwiftUI) ── copia local + sync + outbox ──▶ ┌────────────────────────┐ ──▶ PostgreSQL 18
                                                   │ atm-iris-api (NestJS)  │     (atmosfera-postgres)
  Windows (WinUI) ─ copia local + sync + outbox ─▶ └────────────────────────┘ ──▶ S3 compatible
                                                            │                    (MinIO local · R2 prod)
         Subidas y descargas de archivos: URL firmada, directo al almacenamiento
```

Decisiones que todos respetan:

1. **Un solo contrato** (`api-v1.md`). Nadie inventa rutas ni campos.
2. **Multi-iglesia**: el tenant (`churchId`) sale del token. Un usuario puede pertenecer a varias iglesias.
3. **Permisos, no roles**: los clientes deciden qué mostrar con `permissions` de la sesión.
4. **Consolas sin conexión**: el servicio del domingo no depende de internet. Las consolas leen de su copia local, la ponen al día con `GET /sync/changes` y encolan sus escrituras con ids propios (idempotentes).
5. **Archivos fuera de la API**: subidas y descargas con URLs firmadas. Las consolas guardan los archivos en caché para proyectar sin conexión.
6. **Zona horaria de la iglesia** para "hoy", horarios y resúmenes; todo viaja en UTC.
7. **Borrado suave** en el contenido, para que la sincronización pueda informar lo borrado.
8. **Lo que se proyecta nunca se guarda**: solo los tiempos.

---

## 3. Entorno local

| Servicio | Dónde | Puerto | Cómo |
|---|---|---|---|
| PostgreSQL 18 | Mac, contenedor `atmosfera-postgres` (compartido con otros proyectos) | 5432 | `cd ../atmosfera-postgres && docker compose up -d`. Base `iris`, esquema `iris`, rol `iris_app`, shadow `iris_shadow` |
| MinIO (S3) | Mac, `atm-iris-api/docker-compose.dev.yml` | 9000 (API S3) · 9001 (consola) | `docker compose -f docker-compose.dev.yml up -d` (lo crea la fase 05 de la API) |
| API | Mac | 3020 | `pnpm start:dev` en `atm-iris-api`. Escucha en todas las interfaces para que la PC de Windows llegue por la red local |
| Web | Mac | 3000 | `pnpm dev` en `atm-iris-web` |
| iPad | Mac, simulador | — | Xcode. En el simulador, la API es `http://localhost:3020/api/v1` |
| Windows | PC con Windows | — | Visual Studio / `dotnet build`. **No tiene API durante el desarrollo**: usa su API falsa (ver su plan). Al integrar, apunta a `http://<IP-de-la-Mac>:3020/api/v1` |

Cuenta de desarrollo (la siembra la API): `pastor@vidanueva.org` / `vidanueva123`, iglesia "Iglesia Vida Nueva", rol `owner`.

---

## 4. Cómo trabajan los cuatro agentes

| Agente | Máquina | Repo |
|---|---|---|
| API | Mac | `atm-iris-api` |
| Web | Mac | `atm-iris-web` |
| iPad | Mac | `atm-iris-ios` |
| Windows | PC con Windows | `atm-iris-win` |
| Revisor (al final, uno solo) | Mac, con acceso a la PC de Windows | Los cuatro |

Reglas para **todos** los agentes:

1. **Lee primero**, en este orden: este documento, el contrato de la API, `IRIS_SPEC.md` y el `docs/plans/README.md` de tu repo.
2. **Haz las fases en orden y sin detenerte entre ellas.** Solo te detienes:
   - cuando terminaste **todas** las fases de tu repo (entregas el reporte final, §6), o
   - cuando no puedes seguir sin algo que solo el usuario puede dar (una credencial, una decisión de producto que el plan no resuelve). Pregunta, espera la respuesta y continúa.
3. **Trabaja solo en tu repo.** No edites los otros tres. Si necesitas algo de otro repo, sigue el contrato y anótalo como desviación.
4. **No hagas commits** ni push. El usuario se encarga de git.
5. **Pruebas solo al final.** Durante las fases, la única verificación obligatoria es que **compile** (y el lint o el chequeo de tipos de tu stack). Las pruebas importantes (unitarias, e2e, de interfaz) se escriben y corren en la **última fase**. Si en medio necesitas comprobar algo puntual, vale una verificación rápida en consola (un `curl`, un script corto), pero sin montar suites.
6. **No esperes a los demás.** Los clientes implementan contra el contrato aunque la API todavía no tenga el endpoint. La integración real la hace el revisor al final.
7. **Si el contrato no alcanza**, toma la interpretación más conservadora, sigue y anótala en *Desviaciones* de esa fase. No cambies `api-v1.md`.
8. **Al cerrar cada fase**, marca sus casillas en `docs/plans/README.md`, completa *Desviaciones* (o "Ninguna") y pasa a la siguiente.
9. **Calidad de producto**: textos en español tal como la spec, sin colores ni medidas sueltas, sin dejar `TODO` sin explicar, sin código muerto de la maqueta que ya no se use.

---

## 5. Qué hace el revisor al final

Lo detalla `atm-iris-api/docs/plans/99-revision-final/README.md`. En resumen: levantar todo en local, conectar la web, el iPad y Windows a la API real, recorrer los flujos de punta a punta, corregir las incompatibilidades de contrato y dejar un informe.

---

## 6. Reporte final de cada agente

Al terminar todas las fases, el agente responde con:

```markdown
## Estado de <repo>
- Fases: 00 ✅ · 01 ✅ · …  (o ⚠️ con el motivo)
- Verificación: build/lint/tipos ✅ · pruebas finales: N pasan, M fallan (detalle)
- Desviaciones del contrato o del plan: lista, con archivo y motivo
- Pendiente para la integración: lo que no se pudo probar sin los otros repos
- Cómo correrlo: comandos exactos
```

Y pregunta al usuario si puede dar el repo por terminado.
