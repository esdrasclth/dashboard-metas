import { readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Redis } from "@upstash/redis";
import { eventsBetween, MAX_DEVICES, type DeviceEvent, type DeviceRecord, type DeviceReport } from "./devices.js";

// Screen status storage (Upstash Redis from the Vercel Marketplace):
//   dm:devs               set of device ids
//   dm:dev:<id>           latest record of a screen (JSON)
//   dm:ev:<id>            its last 50 events (list, newest first)
//   dm:seen:<annId>       device id → when it first showed that announcement (hash, 45 days)
//   dm:dis:<annId>        device id → when it was dismissed there (hash, 45 days)
//   dm:rl:<id>            reports per minute (rate limit)
// Without Redis (local `vercel dev` only) the same data lives in a temp JSON file.

const VIEWS_TTL = 45 * 24 * 60 * 60;
const MAX_EVENTS = 50;
const MAX_REPORTS_PER_MINUTE = 6;

interface Kv {
  get(key: string): Promise<string | null>;
  set(key: string, value: string): Promise<void>;
  del(...keys: string[]): Promise<void>;
  sadd(key: string, member: string): Promise<void>;
  srem(key: string, member: string): Promise<void>;
  smembers(key: string): Promise<string[]>;
  scard(key: string): Promise<number>;
  hsetnx(key: string, field: string, value: string, ttl: number): Promise<void>;
  hgetall(key: string): Promise<Record<string, string>>;
  hdel(key: string, field: string): Promise<void>;
  lpushTrim(key: string, values: string[], keep: number): Promise<void>;
  lrange(key: string, count: number): Promise<string[]>;
  incrWithin(key: string, seconds: number): Promise<number>;
}

function redisKv(redis: Redis): Kv {
  return {
    get: async (key) => {
      const value = await redis.get<string | object>(key);
      return value === null || value === undefined ? null : typeof value === "string" ? value : JSON.stringify(value);
    },
    set: async (key, value) => void (await redis.set(key, value)),
    del: async (...keys) => void (await redis.del(...keys)),
    sadd: async (key, member) => void (await redis.sadd(key, member)),
    srem: async (key, member) => void (await redis.srem(key, member)),
    smembers: async (key) => (await redis.smembers(key)) as string[],
    scard: async (key) => await redis.scard(key),
    hsetnx: async (key, field, value, ttl) => {
      const p = redis.pipeline();
      p.hsetnx(key, field, value);
      p.expire(key, ttl);
      await p.exec();
    },
    hgetall: async (key) => ((await redis.hgetall<Record<string, string>>(key)) ?? {}),
    hdel: async (key, field) => void (await redis.hdel(key, field)),
    lpushTrim: async (key, values, keep) => {
      if (!values.length) return;
      const p = redis.pipeline();
      p.lpush(key, ...values);
      p.ltrim(key, 0, keep - 1);
      await p.exec();
    },
    lrange: async (key, count) => ((await redis.lrange<string | object>(key, 0, count - 1)) ?? []).map((v) => (typeof v === "string" ? v : JSON.stringify(v))),
    incrWithin: async (key, seconds) => {
      const p = redis.pipeline();
      p.incr(key);
      p.expire(key, seconds, "NX");
      const [count] = (await p.exec()) as [number, number];
      return count;
    },
  };
}

/**
 * Local development only (`vercel dev` without Redis): one JSON file in the temp folder, so every function
 * process sees the same data. Never used on Vercel.
 */
function fileKv(): Kv {
  type Data = {
    strings: Record<string, string>;
    sets: Record<string, string[]>;
    hashes: Record<string, Record<string, string>>;
    lists: Record<string, string[]>;
    counters: Record<string, { n: number; until: number }>;
  };
  const file = join(tmpdir(), "dashboard-metas-pantallas-dev.json");
  const load = (): Data => {
    try {
      return JSON.parse(readFileSync(file, "utf8"));
    } catch {
      return { strings: {}, sets: {}, hashes: {}, lists: {}, counters: {} };
    }
  };
  const change = <T>(action: (d: Data) => T): T => {
    const d = load();
    const result = action(d);
    writeFileSync(file, JSON.stringify(d));
    return result;
  };
  return {
    get: async (k) => load().strings[k] ?? null,
    set: async (k, v) => change((d) => void (d.strings[k] = v)),
    del: async (...ks) => change((d) => ks.forEach((k) => (delete d.strings[k], delete d.lists[k], delete d.hashes[k]))),
    sadd: async (k, m) => change((d) => void (d.sets[k] = [...new Set([...(d.sets[k] ?? []), m])])),
    srem: async (k, m) => change((d) => void (d.sets[k] = (d.sets[k] ?? []).filter((x) => x !== m))),
    smembers: async (k) => load().sets[k] ?? [],
    scard: async (k) => (load().sets[k] ?? []).length,
    hsetnx: async (k, f, v) => change((d) => {
      const h = (d.hashes[k] ??= {});
      if (!(f in h)) h[f] = v;
    }),
    hgetall: async (k) => load().hashes[k] ?? {},
    hdel: async (k, f) => change((d) => void delete d.hashes[k]?.[f]),
    lpushTrim: async (k, vs, keep) => change((d) => void (d.lists[k] = [...[...vs].reverse(), ...(d.lists[k] ?? [])].slice(0, keep))),
    lrange: async (k, n) => (load().lists[k] ?? []).slice(0, n),
    incrWithin: async (k, s) => change((d) => {
      const now = Date.now();
      const c = d.counters[k];
      d.counters[k] = c && c.until > now ? { n: c.n + 1, until: c.until } : { n: 1, until: now + s * 1000 };
      return d.counters[k].n;
    }),
  };
}

let kv: Kv | null = null;

function store(): Kv {
  if (kv) return kv;
  const url = process.env.KV_REST_API_URL ?? process.env.UPSTASH_REDIS_REST_URL;
  const token = process.env.KV_REST_API_TOKEN ?? process.env.UPSTASH_REDIS_REST_TOKEN;
  if (url && token) {
    kv = redisKv(new Redis({ url, token }));
  } else if (!process.env.VERCEL_ENV || process.env.VERCEL_ENV === "development") {
    kv = fileKv();
  } else {
    throw new Error("Falta la base de datos de pantallas (Upstash Redis) en este proyecto de Vercel.");
  }
  return kv;
}

export const isConfigured = (): boolean =>
  !!((process.env.KV_REST_API_URL ?? process.env.UPSTASH_REDIS_REST_URL) && (process.env.KV_REST_API_TOKEN ?? process.env.UPSTASH_REDIS_REST_TOKEN)) ||
  !process.env.VERCEL_ENV || process.env.VERCEL_ENV === "development";

const parse = <T>(value: string | null): T | null => {
  if (!value) return null;
  try {
    return JSON.parse(value) as T;
  } catch {
    return null;
  }
};

export type SaveResult = { ok: true } | { ok: false; status: number; error: string };

/** Stores a verified report: rate limit, device cap, anti-replay, history of changes and announcement views. */
export async function saveReport(report: DeviceReport, at: string): Promise<SaveResult> {
  const db = store();
  if ((await db.incrWithin(`dm:rl:${report.deviceId}`, 60)) > MAX_REPORTS_PER_MINUTE) {
    return { ok: false, status: 429, error: "Demasiados reportes; espera un minuto." };
  }
  const previous = parse<DeviceRecord>(await db.get(`dm:dev:${report.deviceId}`));
  if (!previous && (await db.scard("dm:devs")) >= MAX_DEVICES) {
    return { ok: false, status: 429, error: `Límite de ${MAX_DEVICES} pantallas alcanzado.` };
  }
  if (previous && Date.parse(report.sentAt) <= Date.parse(previous.report.sentAt)) {
    return { ok: false, status: 409, error: "Reporte repetido o más viejo que el último." };
  }

  const record: DeviceRecord = {
    id: report.deviceId,
    publicKey: report.publicKey,
    label: previous?.label ?? "",
    firstSeen: previous?.firstSeen ?? at,
    lastSeen: at,
    report,
  };
  await db.set(`dm:dev:${report.deviceId}`, JSON.stringify(record));
  await db.sadd("dm:devs", report.deviceId);
  const events = eventsBetween(previous?.report ?? null, report, at);
  await db.lpushTrim(`dm:ev:${report.deviceId}`, events.map((e) => JSON.stringify(e)), MAX_EVENTS);
  for (const [id, when] of Object.entries(report.announcements.seen)) {
    if (!previous?.report.announcements.seen[id]) await db.hsetnx(`dm:seen:${id}`, report.deviceId, when, VIEWS_TTL);
  }
  for (const [id, when] of Object.entries(report.announcements.dismissed)) {
    if (!previous?.report.announcements.dismissed[id]) await db.hsetnx(`dm:dis:${id}`, report.deviceId, when, VIEWS_TTL);
  }
  return { ok: true };
}

export interface ScreenSummary extends DeviceRecord {
  events: DeviceEvent[];
}

export async function listScreens(eventsPerScreen = 20): Promise<ScreenSummary[]> {
  const db = store();
  const ids = await db.smembers("dm:devs");
  const screens = await Promise.all(
    ids.map(async (id) => {
      const record = parse<DeviceRecord>(await db.get(`dm:dev:${id}`));
      if (!record) return null;
      const events = (await db.lrange(`dm:ev:${id}`, eventsPerScreen)).map((e) => parse<DeviceEvent>(e)).filter((e): e is DeviceEvent => !!e);
      return { ...record, events };
    }),
  );
  return screens.filter((s): s is ScreenSummary => !!s).sort((a, b) => (a.label || a.report.machine).localeCompare(b.label || b.report.machine));
}

/** For each announcement: which screens showed it and which dismissed it. */
export async function views(announcementIds: string[]): Promise<Record<string, { seen: string[]; dismissed: string[] }>> {
  const db = store();
  const out: Record<string, { seen: string[]; dismissed: string[] }> = {};
  await Promise.all(
    announcementIds.map(async (id) => {
      out[id] = { seen: Object.keys(await db.hgetall(`dm:seen:${id}`)), dismissed: Object.keys(await db.hgetall(`dm:dis:${id}`)) };
    }),
  );
  return out;
}

export async function renameScreen(id: string, label: string): Promise<boolean> {
  const db = store();
  const record = parse<DeviceRecord>(await db.get(`dm:dev:${id}`));
  if (!record) return false;
  record.label = label.trim().slice(0, 60);
  await db.set(`dm:dev:${id}`, JSON.stringify(record));
  return true;
}

/** Forgets a screen (it comes back on its next report, as new). */
export async function removeScreen(id: string): Promise<void> {
  const db = store();
  await db.srem("dm:devs", id);
  await db.del(`dm:dev:${id}`, `dm:ev:${id}`);
}
