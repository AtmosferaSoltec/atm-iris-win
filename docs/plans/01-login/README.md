# 01 · Login: acceso real y sesión permanente

## Objetivo

Contrato §4.1 y §5: crear cuenta, iniciar sesión **una sola vez** por PC, renovar los tokens solo y
recuperar la contraseña en 3 pasos con código. Si la PC abre sin conexión con una sesión guardada,
entra igual y trabaja con la copia local.

## Dependencias

Fase 00.

## Pasos

### 1. `AuthSessionManager` (`Core/Auth/`)
- Dueño de los tokens. `GetAccessTokenAsync()`: si quedan < 60 s, refresca antes.
- `RefreshAsync()` con **single flight**: un único `Task` en vuelo compartido (protegido con `SemaphoreSlim`).
- `AuthHandler : DelegatingHandler`: pone `Authorization: Bearer`; ante un 401 en una petición que no sea de auth, refresca una
  vez y reintenta; si el refresh responde 401 → evento `SignedOut` (borra el `PasswordVault` y avisa a `SessionStore`); si es
  red/5xx → mantiene la sesión.
- Guarda el refresh en `PasswordVault` y la última `SessionView` (sin tokens) en `LocalFolder/session.json`.

### 2. `LiveAuthService`
Amplía `IAuthService` y su mock: `SignInAsync`, `SignUpAsync`, `SignOutAsync`, `SignOutAllAsync`, `RequestPasswordResetAsync`,
`VerifyResetCodeAsync`, `ResetPasswordAsync`, `SwitchChurchAsync` (fase 03), `CurrentSessionAsync`.
- `client = { platform: "windows", deviceName: Environment.MachineName }`.
- `AuthException` pasa a llevar `Code`, `Message` y `FieldErrors` del API (además de red). Los formularios ponen los errores por
  campo en su `IrisTextField` y el resto en el `IrisBanner`.
- Restaura la validación de "Entrar" (correo y contraseña obligatorios) en `AuthViewModel`.
- Amplía la API falsa con lo que falte de §5.

### 3. Arranque
- `MainWindow`/`SessionStore`: durante el splash, `AuthSessionManager.RestoreAsync()`:
  - sin refresh → acceso;
  - con refresh → entra **de inmediato** con la `SessionView` guardada y refresca en segundo plano; 401 → acceso con el banner
    "Tu sesión expiró. Vuelve a iniciar sesión."; sin red → sigue dentro.
- Cerrar sesión: `SignOutAsync` (si falla por red igual limpia lo local), borra el `PasswordVault` y la sesión guardada.

### 4. Recuperación en 3 pasos
Reemplaza el flujo de enlace (`PasswordRecoveryViewModel` y su `IrisModal`) por tres pasos en el mismo modal:
- Arriba de cada paso: indicador "Paso N de 3" (tres cápsulas, la actual más ancha con el degradado `accent`), emblema circular
  64 con degradado `accent` y el ícono del paso (llave → sobre abierto → escudo), título serif y descripción.
1. **"Recupera tu acceso"** — "Escribe el correo de tu iglesia y te enviaremos un código de 6 dígitos." · correo (precargado) ·
   primario "Enviar código". Siempre avanza.
2. **"Revisa tu correo"** — "Si {correo} tiene una cuenta de Iris, te llegó un código. Vence en 15 minutos." · campo "Código de
   6 dígitos" (solo dígitos, máx. 6, centrado, monoespaciado grande con separación) · "Continuar" · "¿No te llegó? Puedes pedir
   otro en {s} s." → link "Reenviar código" a los 60 s. Errores del API en el campo.
3. **"Crea tu nueva contraseña"** — "Al guardarla cerramos la sesión en todos tus dispositivos y vuelves a iniciar sesión." ·
   "Contraseña nueva" (pista "Mínimo 8 caracteres.") · "Confirma la contraseña" ("Las contraseñas no coinciden.") ·
   primario "Guardar y volver a iniciar sesión". Éxito: cierra el modal y el acceso muestra el banner de éxito
   "Tu contraseña quedó actualizada. Inicia sesión con la nueva."

## Criterios de aceptación

- Modo Fake: crear cuenta, cerrar sesión, entrar, cerrar y abrir la app sin pedir contraseña; esperar 2 min y ver que el refresh
  pasa solo (registro en el log); recuperación completa con `123456`.
- Build limpio.

## Desviaciones

_(Completar al cerrar la fase.)_
