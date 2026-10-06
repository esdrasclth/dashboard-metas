// Instant notice to the screens (Ably). Whenever the control file changes (announcements, a command, new goals or a
// version), a «something changed» message goes to one channel; every screen listening (SSE) checks the control file
// right away. The message carries nothing: what the screens apply still comes signed from this panel, and the goals
// only to approved screens. Without ABLY_API_KEY, or if Ably fails, the screens still check every 30 s.
//
// ABLY_API_KEY: "<app>.<keyId>:<secret>" with publish + subscribe on "dm:*". The screens never get it: they get a
// short-lived token that can only subscribe to that one channel.

const REST = "https://rest.ably.io";
export const SSE_URL = "https://realtime.ably.io/sse";
const TOKEN_HOURS = 12;

const key = () => process.env.ABLY_API_KEY ?? "";
const prefix = () => (process.env.STORE_PREFIX ?? "").replace(/^\/+/, "");

export const isRealtimeConfigured = (): boolean => /^[^.:\s]+\.[^:\s]+:\S+$/.test(key());

/** One channel for every screen («dev» while testing with `vercel dev`). */
export const channel = (): string => (prefix() ? "dm:dev:avisos" : "dm:avisos");

function basic(): string {
  return `Basic ${Buffer.from(key()).toString("base64")}`;
}

/** «Something changed»: never throws and never waits more than 3 s (the change is already saved). */
export async function notify(what: string): Promise<void> {
  if (!isRealtimeConfigured()) return;
  try {
    const response = await fetch(`${REST}/channels/${encodeURIComponent(channel())}/messages`, {
      method: "POST",
      headers: { authorization: basic(), "content-type": "application/json" },
      body: JSON.stringify({ name: "cambio", data: JSON.stringify({ what, at: new Date().toISOString() }) }),
      signal: AbortSignal.timeout(3000),
    });
    if (!response.ok) console.error(`Aviso instantáneo: Ably respondió ${response.status}`);
  } catch (error) {
    console.error("Aviso instantáneo no enviado", error);
  }
}

export interface ScreenToken {
  token: string;
  expires: string;
  channel: string;
  url: string;
}

/** A token that can only listen to the screens' channel, for a few hours. Null if Ably is not set up or fails. */
export async function screenToken(): Promise<ScreenToken | null> {
  if (!isRealtimeConfigured()) return null;
  const keyName = key().slice(0, key().indexOf(":"));
  try {
    const response = await fetch(`${REST}/keys/${encodeURIComponent(keyName)}/requestToken`, {
      method: "POST",
      headers: { authorization: basic(), "content-type": "application/json" },
      body: JSON.stringify({
        keyName,
        capability: JSON.stringify({ [channel()]: ["subscribe"] }),
        ttl: TOKEN_HOURS * 3600_000,
        timestamp: Date.now(),
      }),
      signal: AbortSignal.timeout(4000),
    });
    if (!response.ok) {
      console.error(`Ably no emitió el permiso de escucha: ${response.status}`);
      return null;
    }
    const details = (await response.json()) as { token?: string; expires?: number };
    if (!details.token || !details.expires) return null;
    return { token: details.token, expires: new Date(details.expires).toISOString(), channel: channel(), url: SSE_URL };
  } catch (error) {
    console.error("Ably no emitió el permiso de escucha", error);
    return null;
  }
}
