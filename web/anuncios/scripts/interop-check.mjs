// Signs a feed with the same browser code (public/signer.js) and writes control.json, so the .NET tool can verify it:
//   node scripts/interop-check.mjs <clave-privada.pem> <salida.json>
//   anuncios verificar <salida.json>
// If .NET accepts it, the panel's signatures are byte-compatible with the app.
import { readFileSync, writeFileSync } from "node:fs";
import { importPrivateKeyPem, signFeed } from "../public/signer.js";
import { validateFeed } from "../public/rules.js";

const [pemPath, outPath = "control-interop.json"] = process.argv.slice(2);
if (!pemPath) {
  console.error("Uso: node scripts/interop-check.mjs <clave-privada.pem> [salida.json]");
  process.exit(2);
}

const now = new Date();
const offset = (date) => {
  const minutes = -date.getTimezoneOffset();
  const sign = minutes >= 0 ? "+" : "-";
  const abs = Math.abs(minutes);
  const pad = (n) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:00${sign}${pad(Math.floor(abs / 60))}:${pad(abs % 60)}`;
};
const feed = {
  version: Math.floor(now.getTime() / 1000),
  issuedAt: offset(now),
  announcements: [
    {
      id: "prueba-interop",
      title: "Prueba de compatibilidad «ñ» y acentos: áéíóú",
      message: "Firmado con Web Crypto (el mismo código del panel).\nSegunda línea.",
      severity: "Warning",
      display: "Banner",
      startsAt: offset(now),
      endsAt: offset(new Date(now.getTime() + 3600_000)),
      targets: ["planta:027"],
      dismissible: true,
      sound: false,
    },
  ],
};
const problems = validateFeed(feed);
if (problems.length) throw new Error(problems.join("\n"));
const { privateKey, keyId } = await importPrivateKeyPem(readFileSync(pemPath, "utf8"));
writeFileSync(outPath, await signFeed(feed, privateKey, keyId));
console.log(`Firmado con la clave ${keyId}: ${outPath}`);
