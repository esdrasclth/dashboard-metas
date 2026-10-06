# Panel de anuncios

Página web para redactar y publicar los anuncios que Dashboard Metas muestra en las pantallas.

**Producción:** https://dashboard-metas-anuncios.vercel.app — las pantallas leen `/control.json`.

## Acceso

| | |
| --- | --- |
| **Panel** | https://dashboard-metas-anuncios.vercel.app |
| **Contraseña** | En `ACCESO.local.md` de esta carpeta (no se sube a git: el repositorio es público) |
| **Clave de firma** | `Documentos\DashboardMetas-Anuncios\clave-privada-anuncios.pem` (no está en el repo) |

> La contraseña sola no permite publicar: también hace falta la clave de firma. Nunca escribas la contraseña en un
> archivo que se suba a git; si se filtra, cámbiala (ver [Cambiar la contraseña](#cambiar-la-contraseña)).

## Cómo funciona

```text
navegador (panel)                      Vercel                                   pantallas
  redactas → borrador (en el navegador)
  Publicar → firma con tu clave ──POST──> /api/publish: verifica firma,
             (la clave no sale)            reglas y versión → Blob privado ──> /control.json (cada 30 s)
```

- **Dos llaves para publicar:** la contraseña del panel y la clave privada de firma. La clave se carga una vez en el
  navegador como `CryptoKey` no exportable (IndexedDB) y firma ahí; nunca se envía. Con solo la contraseña no se puede
  publicar nada que las pantallas acepten.
- **El servidor no confía en el navegador:** antes de guardar comprueba la firma con la clave pública (la misma de
  `AnnouncementKeys.vb`), las reglas de `public/rules.js` (las mismas de la app) y que la versión sea más nueva.
- **Almacenamiento:** Vercel Blob privado. `control/actual.json` es lo que se sirve; cada publicación queda además en
  `historial/<versión>.json` (se ve y se recupera desde el panel).
- **Estado de las pantallas:** cada pantalla envía un reporte firmado con su propia clave a `POST /api/heartbeat`
  (público: las pantallas no tienen sesión). El id de la pantalla es la huella de su clave pública, así que un reporte
  solo puede actualizar su propia pantalla; además se exige hora cercana a la del servidor (±15 min), cada reporte más
  nuevo que el anterior, máximo 6 por minuto por pantalla y 200 pantallas. Se guarda en **Upstash Redis**: el último
  reporte de cada pantalla, sus últimos 50 eventos y quién vio o cerró cada anuncio (45 días).
- **Versiones:** `npm run publicar-version` sube el zip de `publish\DashboardMetas` a `versiones/paquetes/` (Blob
  privado) y publica un manifiesto firmado (versión, tamaño, SHA-256, destinos, fuera de turno o ahora). `POST /api/release`
  verifica la firma y que la publicación sea más nueva. Lo guarda en `versiones/actual.json` y en el historial. La
  respuesta de `/api/heartbeat` le pasa a cada pantalla que le toca el manifiesto y un enlace de descarga temporal.
  Pausar, cambiar destinos o volver atrás desde la pestaña **Versiones** es otro manifiesto firmado en el navegador.
- **Metas y turnos:** el panel firma la configuración en el navegador (`dashboardmetas-metas/1`) y `POST /api/config`
  la verifica (firma, áreas conocidas, metas y horarios válidos con las mismas reglas de la app, revisión más nueva).
  Se guarda en Blob privado (`metas/actual.json` + `metas/historial/`). **Nunca va en `/control.json`**, que es público:
  la respuesta de `/api/heartbeat` la entrega solo a las pantallas **aprobadas** (`POST /api/screens`
  `{action:"trust"}`) y solo cuando la suya no es la actual.
- **Comandos:** firmados en el navegador (`dashboardmetas-comando/1`: id, vencimiento ≤ 60 min, destinos = ids de
  pantalla o todas, acción). `POST /api/commands` los verifica y los agrega a `control/actual.json` (campo `commands`,
  junto a los anuncios), así cada pantalla los recibe en su siguiente consulta (≤ 30 s) sin costo extra. Los vencidos
  se descartan al escribir. Las pantallas devuelven el resultado en su reporte. Las versiones de la app sin comandos
  ignoran ese campo.
- **Señales:** al guardar metas o una versión, `control/actual.json` recibe `signals: {config, release}` (solo los
  números de revisión). La pantalla que ve un número nuevo reporta al momento y recibe lo nuevo en la respuesta, en ~30 s
  en vez de esperar su reporte de cada 5 minutos.
- **Avisos instantáneos (Ably):** cada vez que cambia `control/actual.json`, `lib/realtime.ts` publica «cambio» en el canal
  `dm:avisos` (en local `dm:dev:avisos`). La respuesta de `/api/heartbeat` le da a cada pantalla de versión 1.4 o más nueva
  un token de **solo escucha** de ese canal (12 h; se renueva antes de vencer). La pantalla escucha por SSE y al recibir el
  aviso consulta `/control.json`. Sin `ABLY_API_KEY`, o si Ably falla, todo sigue con la consulta de cada 30 s.
- `/control.json` es público a propósito (solo trae anuncios firmados que las pantallas muestran igual) y responde con
  ETag y `no-cache`: una pantalla sin cambios recibe 304.

## Estructura

```text
api/        login, logout, state, publish, history, control, heartbeat, screens, release, config, commands
lib/        sesión (scrypt + cookie firmada), verificación de firma, almacenamiento, pantallas, versiones, remote
public/     el panel (HTML, CSS, JS sin dependencias); rules.js, releases.js y remote.js los comparten el panel y la API
scripts/    hash-password.mjs (contraseña), interop-check.mjs (firma compatible con la app),
            publish-version.mjs (npm run publicar-version: sube el paquete y firma la versión)
```

## Variables de entorno (Vercel)

| Variable | Qué es |
| --- | --- |
| `ADMIN_PASSWORD_HASH` | Hash scrypt de la contraseña del panel (`scrypt:N:r:p:sal:hash`) |
| `SESSION_SECRET` | Secreto para firmar la cookie de sesión (12 h) |
| `BLOB_READ_WRITE_TOKEN` | Lo agrega Vercel al conectar el almacén Blob `dashboard-metas-anuncios` |
| `ABLY_API_KEY` | Clave de Ably (`app.clave:secreto`, publicar y escuchar en `dm:*`) para los avisos instantáneos. Sin ella, todo funciona con la consulta de 30 s |
| `KV_REST_API_URL` · `KV_REST_API_TOKEN` | Los agrega Vercel al conectar **Upstash for Redis** (estado de las pantallas). Sin ellos la pestaña Pantallas muestra un aviso y los reportes responden 503 |

### Cambiar la contraseña

```cmd
npm run hash-password -- "nueva contraseña"
vercel env add ADMIN_PASSWORD_HASH production --sensitive --force   :: pegar el valor que imprimió
vercel deploy --prod
```

Sin argumento, `hash-password` genera una contraseña aleatoria fuerte. Cambiar también `SESSION_SECRET` cierra todas
las sesiones abiertas.

## Desarrollo

```cmd
npm install
npm run typecheck
vercel dev            :: http://localhost:3000 (mismo almacén Blob, pero bajo dev/ si .env tiene STORE_PREFIX=dev/)
```

Para `vercel dev` las variables locales van en `.env` (no versionado): `ADMIN_PASSWORD_HASH`, `SESSION_SECRET`,
`BLOB_READ_WRITE_TOKEN`, `ABLY_API_KEY` y `STORE_PREFIX=dev/`. Con el prefijo, todo lo que se escribe en local (archivo de control,
comandos, metas, versiones) queda bajo `dev/` y las pantallas reales no lo ven. Sin Redis, `vercel dev` guarda las pantallas en un JSON temporal
(`%TEMP%\dashboard-metas-pantallas-dev.json`); para probar, poner en la app `Status:Url` =
`http://localhost:3000/api/heartbeat`.

**Compatibilidad de la firma con la app** (después de tocar `signer.js` o `rules.js`):

```cmd
node scripts/interop-check.mjs "%USERPROFILE%\Documents\DashboardMetas-Anuncios\clave-privada-anuncios.pem" prueba.json
dotnet run --project ..\..\tools\DashboardMetas.Anuncios -- verificar prueba.json
```

## Desplegar

```cmd
vercel deploy --prod
```

Si se agrega una clave pública nueva a la app (`AnnouncementKeys.vb`), agregarla también en `lib/signing.ts`
(`TRUSTED_KEYS`) y desplegar.
