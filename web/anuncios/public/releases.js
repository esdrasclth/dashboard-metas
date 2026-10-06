// Version manifests (automatic updates): one source for the panel, the API and the publish script.
// Same rules as DashboardMetas.Core/Updates/ReleaseManifest.vb.

export const RELEASE_FORMAT = "dashboardmetas-version/1";
export const INSTALL_OFF_SHIFT = "fuera-de-turno";
export const INSTALL_NOW = "ahora";
export const MAX_RELEASE_BYTES = 200 * 1024 * 1024;

const VERSION = /^\d{1,4}\.\d{1,4}\.\d{1,6}$/;
const HASH = /^[0-9a-f]{64}$/;

/** "1.10.0" vs "1.9.2" → positive / 0 / negative. */
export function compareVersions(a, b) {
  const x = String(a).split(".").map(Number);
  const y = String(b).split(".").map(Number);
  for (let i = 0; i < 3; i++) {
    const d = (x[i] ?? 0) - (y[i] ?? 0);
    if (d) return d;
  }
  return 0;
}

/** @param {any} m @returns {string[]} problems in Spanish (empty = valid) */
export function validateManifest(m) {
  const errors = [];
  if (!m || typeof m !== "object") return ["Manifiesto vacío."];
  if (!Number.isSafeInteger(m.release) || m.release <= 0) errors.push("Falta el número de publicación.");
  if (typeof m.version !== "string" || !VERSION.test(m.version)) errors.push("La versión debe ser como 1.2.0.");
  const f = m.file;
  if (!f || typeof f !== "object") {
    errors.push("Falta el archivo.");
  } else {
    if (typeof f.pathname !== "string" || !f.pathname.startsWith("versiones/") || !f.pathname.toLowerCase().endsWith(".zip")) {
      errors.push("El archivo debe ser un .zip dentro de «versiones/».");
    }
    if (!Number.isSafeInteger(f.size) || f.size <= 0 || f.size > MAX_RELEASE_BYTES) errors.push("Tamaño de archivo no válido.");
    if (typeof f.sha256 !== "string" || !HASH.test(f.sha256)) errors.push("La huella SHA-256 no es válida.");
  }
  if (m.install !== INSTALL_OFF_SHIFT && m.install !== INSTALL_NOW) errors.push("La instalación debe ser «fuera-de-turno» o «ahora».");
  if (typeof m.notes !== "string" || m.notes.length > 2000) errors.push("Las notas pasan de 2,000 caracteres.");
  if (!Array.isArray(m.targets) || m.targets.length > 100 || m.targets.some((t) => typeof t !== "string" || !t.trim())) errors.push("Destinos no válidos.");
  if (typeof m.paused !== "boolean" || typeof m.allowDowngrade !== "boolean") errors.push("paused y allowDowngrade deben ser true o false.");
  if (typeof m.publishedAt !== "string" || Number.isNaN(Date.parse(m.publishedAt))) errors.push("Fecha de publicación no válida.");
  return errors;
}

/** Same targeting as the announcements: empty or "*" = all; "planta:027"; "equipo:NAME" (or the bare name). */
export function isForScreen(targets, machine, branch) {
  const list = (targets ?? []).map((t) => String(t).trim()).filter(Boolean);
  if (!list.length || list.includes("*")) return true;
  return list.some((t) => {
    const colon = t.indexOf(":");
    const kind = colon > 0 ? t.slice(0, colon).trim().toLowerCase() : "equipo";
    const value = (colon > 0 ? t.slice(colon + 1) : t).trim().toLowerCase();
    if (!value) return false;
    if (kind === "planta" || kind === "plant") return value === String(branch ?? "").toLowerCase();
    return value === String(machine ?? "").toLowerCase();
  });
}
