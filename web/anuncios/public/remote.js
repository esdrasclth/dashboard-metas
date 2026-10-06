// «Metas y turnos» and remote commands: one source for the panel and the API.
// Same rules as DashboardMetas.Core/Remote/RemoteConfig.vb and RemoteCommand.vb.

export const CONFIG_FORMAT = "dashboardmetas-metas/1";
export const COMMAND_FORMAT = "dashboardmetas-comando/1";

/** The plant's areas (AreaCatalog.Defaults): code → default name. */
export const AREAS = [
  ["Y1", "Entregas a Ensamble"],
  ["Y2", "Estación Y2"],
  ["Y3", "Estación Y3"],
  ["Y5", "Estación Y5"],
  ["Y7", "Estación Y7"],
  ["SHP", "Embarques"],
];
const AREA_CODES = new Set(AREAS.map(([code]) => code));
const MAX_GOAL = 1_000_000_000;

export const ACTION_REFRESH = "actualizar";
export const ACTION_RESTART = "reiniciar";
export const ACTION_SHOW = "mostrar";
export const VIEWS = { float: "float", overview: "general", area: "area", rotation: "rotacion" };
export const COMMAND_MINUTES = 10;
const MAX_LIFETIME_MINUTES = 60;
const MAX_HOLD_MINUTES = 480;

/** "HH:mm" → minutes, or null. */
function minutes(text) {
  const m = /^(\d{1,2}):(\d{2})$/.exec(String(text ?? "").trim());
  if (!m) return null;
  const h = Number(m[1]);
  const min = Number(m[2]);
  return h < 24 && min < 60 ? h * 60 + min : null;
}

/** Same checks as ShiftSchedule.TryCreate: valid hours, start ≠ end, break both or none and inside the shift. */
export function shiftProblem(start, end, breakStart, breakEnd) {
  const s = minutes(start);
  const e = minutes(end);
  if (s === null || e === null) return "El turno debe tener inicio y fin en formato HH:mm.";
  if (s === e) return "El inicio y el fin del turno no pueden ser iguales.";
  const hasBreak = String(breakStart ?? "").trim() !== "" || String(breakEnd ?? "").trim() !== "";
  if (!hasBreak) return null;
  const b0 = minutes(breakStart);
  const b1 = minutes(breakEnd);
  if (b0 === null || b1 === null || b0 === b1) return "La pausa debe tener inicio y fin en formato HH:mm (o dejar ambos vacíos).";
  // On the shift timeline (from its start), the break must fit inside it — exactly like ShiftSchedule.TryCreate
  const em = e + (e <= s ? 1440 : 0);
  const bs = b0 + (b0 < s ? 1440 : 0);
  const be = b1 + (b1 < s ? 1440 : 0);
  if (be <= bs || bs < s || be > em || be - bs >= em - s) return "La pausa debe quedar dentro del turno.";
  return null;
}

/** @returns {string[]} problems in Spanish (empty = valid) */
export function validateConfig(c) {
  const errors = [];
  if (!c || typeof c !== "object") return ["Configuración vacía."];
  if (!Number.isSafeInteger(c.revision) || c.revision <= 0) errors.push("Falta el número de revisión.");
  if (typeof c.publishedAt !== "string" || Number.isNaN(Date.parse(c.publishedAt))) errors.push("Fecha de publicación no válida.");
  if (!Array.isArray(c.areas)) return [...errors, "Faltan las áreas."];
  if (c.areas.length > 20) errors.push("Máximo 20 áreas.");
  const seen = new Set();
  for (const a of c.areas) {
    const code = String(a?.code ?? "").trim().toUpperCase();
    if (!AREA_CODES.has(code)) {
      errors.push(`Área «${a?.code ?? ""}» desconocida.`);
      continue;
    }
    if (seen.has(code)) errors.push(`El área ${code} está repetida.`);
    seen.add(code);
    if (typeof a.dailyGoal !== "number" || !(a.dailyGoal > 0) || a.dailyGoal > MAX_GOAL) errors.push(`${code}: la meta diaria debe ser mayor que 0.`);
    if (typeof a.monthlyGoal !== "number" || a.monthlyGoal < 0 || a.monthlyGoal > MAX_GOAL) errors.push(`${code}: la meta mensual no es válida.`);
    const problem = shiftProblem(a.shiftStart, a.shiftEnd, a.breakStart, a.breakEnd);
    if (problem) errors.push(`${code}: ${problem}`);
  }
  return errors;
}

/** @returns {string[]} problems in Spanish (empty = valid) */
export function validateCommand(c) {
  const errors = [];
  if (!c || typeof c !== "object") return ["Comando vacío."];
  if (typeof c.id !== "string" || !c.id || c.id.length > 64) errors.push("Falta el id del comando.");
  const issued = Date.parse(c.issuedAt);
  const expires = Date.parse(c.expiresAt);
  if (Number.isNaN(issued) || Number.isNaN(expires) || expires <= issued || expires - issued > MAX_LIFETIME_MINUTES * 60_000) {
    errors.push(`El comando debe vencer después de enviarse y en ${MAX_LIFETIME_MINUTES} minutos o menos.`);
  }
  if (!Array.isArray(c.targets) || c.targets.length > 200 || c.targets.some((t) => typeof t !== "string")) errors.push("Destinos no válidos.");
  if (c.action === ACTION_SHOW) {
    if (!Object.values(VIEWS).includes(c.view)) errors.push(`Vista «${c.view}» desconocida.`);
    if (c.view === VIEWS.area && !AREA_CODES.has(String(c.area ?? "").toUpperCase())) errors.push(`Área «${c.area ?? ""}» desconocida.`);
    if (!Number.isInteger(c.holdMinutes) || c.holdMinutes < 0 || c.holdMinutes > MAX_HOLD_MINUTES) errors.push(`Se puede mantener entre 0 y ${MAX_HOLD_MINUTES} minutos.`);
  } else if (c.action !== ACTION_REFRESH && c.action !== ACTION_RESTART) {
    errors.push(`Acción «${c.action}» desconocida.`);
  }
  return errors;
}

/** «Mostrar el Float durante 30 min», like RemoteCommand.Describe. */
export function describeCommand(c, areaName = (code) => code) {
  if (c.action === ACTION_REFRESH) return "Actualizar los datos";
  if (c.action === ACTION_RESTART) return "Reiniciar la app";
  if (c.view === VIEWS.rotation) return "Volver a la rotación normal";
  const what = c.view === VIEWS.float ? "el Float" : c.view === VIEWS.overview ? "la vista general" : `${areaName(c.area)}`;
  return `Mostrar ${what}${c.holdMinutes > 0 ? ` durante ${c.holdMinutes} min` : ""}`;
}
