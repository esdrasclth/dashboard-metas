// Announcement rules: one source for the panel (browser) and the API (server).
// Same limits and messages as DashboardMetas.Core/Announcements/AnnouncementRules.vb:
// a file the panel accepts is a file the app accepts.

export const FORMAT = "dashboardmetas-anuncios/1";
export const MAX_ANNOUNCEMENTS = 20;
export const MAX_TITLE = 120;
export const MAX_MESSAGE = 1500;
export const MAX_TARGETS = 100;
export const MAX_DURATION_MS = 31 * 24 * 60 * 60 * 1000;
export const MAX_FILE_BYTES = 256 * 1024;

const ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;
const SEVERITIES = new Set(["info", "warning", "critical"]);
const DISPLAYS = new Set(["modal", "banner"]);

/** @param {unknown} value @returns {number | null} null = empty, NaN = invalid (an offset is required) */
export function parseTime(value) {
  if (typeof value !== "string" || value.trim() === "") return null;
  if (!/([+-]\d{2}:\d{2}|Z)$/.test(value)) return NaN;
  const ms = Date.parse(value);
  return Number.isNaN(ms) ? NaN : ms;
}

function checkLength(errors, name, what, value, max) {
  if (typeof value === "string" && value.length > max) {
    errors.push(`${name}: el ${what} tiene ${value.length} caracteres (máximo ${max}).`);
  }
}

/** @param {any} a @returns {string[]} problems in Spanish (empty = valid) */
export function validateAnnouncement(a) {
  const errors = [];
  if (!a || typeof a !== "object") return ["Anuncio vacío."];
  const name = typeof a.id === "string" && a.id.trim() ? a.id : "(sin id)";
  if (typeof a.id !== "string" || !ID_PATTERN.test(a.id)) {
    errors.push(`${name}: el id debe tener de 1 a 64 letras, números, punto, guion o guion bajo.`);
  }
  if (typeof a.title !== "string" || !a.title.trim()) errors.push(`${name}: falta el título.`);
  if (typeof a.message !== "string" || !a.message.trim()) errors.push(`${name}: falta el mensaje.`);
  checkLength(errors, name, "título", a.title, MAX_TITLE);
  checkLength(errors, name, "título en inglés", a.titleEn, MAX_TITLE);
  checkLength(errors, name, "mensaje", a.message, MAX_MESSAGE);
  checkLength(errors, name, "mensaje en inglés", a.messageEn, MAX_MESSAGE);
  if (typeof a.severity !== "string" || !SEVERITIES.has(a.severity.toLowerCase())) {
    errors.push(`${name}: severidad no válida (info, warning o critical).`);
  }
  if (typeof a.display !== "string" || !DISPLAYS.has(a.display.toLowerCase())) {
    errors.push(`${name}: forma no válida (modal o banner).`);
  }
  const starts = parseTime(a.startsAt);
  const ends = parseTime(a.endsAt);
  if (Number.isNaN(starts)) errors.push(`${name}: startsAt no es una fecha con zona horaria.`);
  if (ends === null) {
    errors.push(`${name}: falta endsAt (fecha y hora en que deja de mostrarse).`);
  } else if (Number.isNaN(ends)) {
    errors.push(`${name}: endsAt no es una fecha con zona horaria.`);
  } else if (starts !== null && !Number.isNaN(starts)) {
    if (ends <= starts) errors.push(`${name}: endsAt debe ser posterior a startsAt.`);
    if (ends - starts > MAX_DURATION_MS) errors.push(`${name}: no puede mostrarse más de 31 días.`);
  }
  if (a.targets !== undefined && !Array.isArray(a.targets)) errors.push(`${name}: targets debe ser una lista.`);
  const targets = Array.isArray(a.targets) ? a.targets : [];
  if (targets.length > MAX_TARGETS) errors.push(`${name}: máximo ${MAX_TARGETS} destinos.`);
  if (targets.some((t) => typeof t !== "string" || !t.trim())) errors.push(`${name}: hay un destino vacío.`);
  if (a.dismissible !== undefined && typeof a.dismissible !== "boolean") errors.push(`${name}: dismissible debe ser true o false.`);
  if (a.sound !== undefined && typeof a.sound !== "boolean") errors.push(`${name}: sound debe ser true o false.`);
  return errors;
}

/** @param {any} feed @returns {string[]} */
export function validateFeed(feed) {
  if (!feed || typeof feed !== "object") return ["El archivo no tiene anuncios."];
  const errors = [];
  if (!Number.isSafeInteger(feed.version) || feed.version <= 0) errors.push("La versión debe ser un entero positivo.");
  const issued = parseTime(feed.issuedAt);
  if (issued === null || Number.isNaN(issued)) errors.push("issuedAt debe ser una fecha con zona horaria.");
  if (!Array.isArray(feed.announcements)) return [...errors, "Falta la lista de anuncios."];
  const list = feed.announcements;
  if (list.length > MAX_ANNOUNCEMENTS) errors.push(`Máximo ${MAX_ANNOUNCEMENTS} anuncios por archivo (hay ${list.length}).`);
  const seen = new Map();
  for (const a of list) {
    if (a && typeof a.id === "string") seen.set(a.id.toLowerCase(), (seen.get(a.id.toLowerCase()) ?? 0) + 1);
  }
  for (const [id, count] of seen) if (count > 1) errors.push(`El id «${id}» está repetido.`);
  for (const a of list) errors.push(...validateAnnouncement(a));
  return errors;
}
