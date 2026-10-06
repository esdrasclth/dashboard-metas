import { guard, json, safe } from "../lib/http.js";
import { payloadOf, TRUSTED_KEYS } from "../lib/signing.js";
import { readCurrent } from "../lib/store.js";

/** What is published now (decoded), the keys the app accepts and the address to put in the app. */
export const GET = safe(async (request) => {
  const denied = guard(request);
  if (denied) return denied;
  const current = await readCurrent();
  const published =
    current && current !== "not-modified"
      ? { ...payloadOf(current.signed), keyId: current.signed.keyId }
      : null;
  return json({
    published,
    trustedKeys: Object.keys(TRUSTED_KEYS),
    feedUrl: new URL("/control.json", request.url).toString(),
    serverTime: new Date().toISOString(),
  });
});
