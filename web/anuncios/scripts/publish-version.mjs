// Publishes a new version of Dashboard Metas for the screens to install by themselves.
//
//   npm run publicar-version -- --notas "Qué cambió" [--ahora] [--destinos "equipo:TV-01"] [--pausa]
//
// 1. Packs publish\DashboardMetas\DashboardMetas.exe + appsettings.json (never appsettings.empresa.json: every
//    PC keeps its own) into a zip, checks the exe's version and computes its SHA-256.
// 2. Uploads the zip to the panel's private Blob store (versiones/paquetes/…).
// 3. Signs the manifest with clave-privada-anuncios.pem (same code as the panel) and posts it to /api/release,
//    which stores it only if the app would accept it.
// Needs: publicar.cmd run first; BLOB_READ_WRITE_TOKEN (vercel env pull) and the panel password
// (PANEL_PASSWORD or asked here). Runs on Windows (PowerShell) or Linux (zip; see publicar.sh). --panel http://localhost:3000 publishes to `vercel dev` under «dev/» instead.

import { execFileSync } from "node:child_process";
import { createHash, randomBytes } from "node:crypto";
import { createReadStream, existsSync, readFileSync, statSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { createInterface } from "node:readline/promises";
import { put } from "@vercel/blob";
import { INSTALL_NOW, INSTALL_OFF_SHIFT, RELEASE_FORMAT, validateManifest } from "../public/releases.js";
import { importPrivateKeyPem, signEnvelope } from "../public/signer.js";

const here = resolve(import.meta.dirname, "..");
const repo = resolve(here, "..", "..");

function option(name, fallback) {
  const i = process.argv.indexOf(name);
  return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : fallback;
}
const flag = (name) => process.argv.includes(name);
const windows = process.platform === "win32";
// On Linux the key and the secrets (PANEL_PASSWORD, BLOB_READ_WRITE_TOKEN) live outside the repo, readable only by the user.
const linuxConfig = join(homedir(), ".config", "dashboard-metas");

// FileVersion of a Windows exe without PowerShell (to publish from Linux), as Windows reads it: walks the exe's own
// resource tree to RT_VERSION. Searching the file for the signature is not enough: the single-file bundle and other
// resources carry other versions. VS_FIXEDFILEINFO starts with 0xFEEF04BD, then struct version, FileVersionMS/LS.
function peFileVersion(file) {
  const bytes = readFileSync(file);
  const pe = bytes.readUInt32LE(0x3c);
  const table = pe + 24 + bytes.readUInt16LE(pe + 20);
  for (let i = 0; i < bytes.readUInt16LE(pe + 6); i++) {
    const header = table + i * 40;
    if (bytes.toString("latin1", header, header + 8).replace(/\0+$/, "") !== ".rsrc") continue;
    const va = bytes.readUInt32LE(header + 12), raw = bytes.readUInt32LE(header + 20);
    // First entry of a directory whose id is `id` (or the first one when id is undefined): offset of what it points to.
    const pick = (dir, id) => {
      const count = bytes.readUInt16LE(raw + dir + 12) + bytes.readUInt16LE(raw + dir + 14);
      for (let e = 0; e < count; e++) {
        const entry = raw + dir + 16 + e * 8;
        if (id === undefined || bytes.readUInt32LE(entry) === id) return bytes.readUInt32LE(entry + 4) & 0x7fffffff;
      }
      return -1;
    };
    const names = pick(0, 16); // RT_VERSION
    const langs = names < 0 ? -1 : pick(names);
    const data = langs < 0 ? -1 : pick(langs);
    if (data < 0) break;
    const at = bytes.readUInt32LE(raw + data) - va + raw;
    const block = bytes.subarray(at, at + bytes.readUInt32LE(raw + data + 4));
    const fixed = block.indexOf(Buffer.from([0xbd, 0x04, 0xef, 0xfe]));
    if (fixed < 0) break;
    const ms = block.readUInt32LE(fixed + 8), ls = block.readUInt32LE(fixed + 12);
    return `${ms >>> 16}.${ms & 0xffff}.${ls >>> 16}.${ls & 0xffff}`;
  }
  fail(`No se pudo leer la versión de ${file}.`);
}

function loadEnv(file) {
  if (!existsSync(file)) return;
  for (const line of readFileSync(file, "utf8").split(/\r?\n/)) {
    const m = /^\s*([A-Z0-9_]+)\s*=\s*(.*)\s*$/.exec(line);
    if (m && !process.env[m[1]]) process.env[m[1]] = m[2].replace(/^["']|["']$/g, "");
  }
}

function fail(message) {
  console.error(`✕ ${message}`);
  process.exit(1);
}

async function main() {
  loadEnv(join(here, ".env.local"));
  loadEnv(process.env.DASHBOARDMETAS_ENV ?? join(linuxConfig, "publicar.env"));
  const panel = (option("--panel", "https://dashboard-metas-anuncios.vercel.app")).replace(/\/+$/, "");
  const local = /^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(panel);
  if (local) loadEnv(join(here, ".env"));
  const prefix = local ? "dev/" : "";

  // ---- the build
  const folder = resolve(option("--carpeta", join(repo, "publish", "DashboardMetas")));
  const exe = join(folder, "DashboardMetas.exe");
  const settings = join(folder, "appsettings.json");
  if (!existsSync(exe) || !existsSync(settings)) fail(`No está ${exe} o appsettings.json: corre publicar.cmd primero.`);
  const props = readFileSync(join(repo, "Directory.Build.props"), "utf8");
  const version = option("--version", /<Version>([^<]+)<\/Version>/.exec(props)?.[1]);
  const fileVersion = windows
    ? execFileSync("powershell", ["-NoProfile", "-Command", `(Get-Item -LiteralPath '${exe.replace(/'/g, "''")}').VersionInfo.FileVersion`]).toString().trim()
    : peFileVersion(exe);
  if (!fileVersion.startsWith(`${version}.`) && fileVersion !== version) fail(`El exe es la versión ${fileVersion}, no ${version}: vuelve a correr publicar.cmd.`);

  const zip = join(tmpdir(), `DashboardMetas-${version}-${randomBytes(4).toString("hex")}.zip`);
  if (windows) execFileSync("powershell", ["-NoProfile", "-Command", `Compress-Archive -LiteralPath '${exe.replace(/'/g, "''")}','${settings.replace(/'/g, "''")}' -DestinationPath '${zip.replace(/'/g, "''")}' -Force`]);
  else execFileSync("zip", ["-j", "-q", zip, exe, settings]);
  const size = statSync(zip).size;
  const sha256 = createHash("sha256").update(readFileSync(zip)).digest("hex");
  console.log(`Paquete ${version}: ${(size / 1024 / 1024).toFixed(1)} MB · SHA-256 ${sha256.slice(0, 16)}…`);

  // ---- key and password
  const keyPath = option("--clave", process.env.DASHBOARDMETAS_CLAVE_ANUNCIOS ?? join(windows ? join(homedir(), "Documents", "DashboardMetas-Anuncios") : linuxConfig, "clave-privada-anuncios.pem"));
  if (!existsSync(keyPath)) fail(`No se encontró la clave privada en ${keyPath}.`);
  const { privateKey, keyId } = await importPrivateKeyPem(readFileSync(keyPath, "utf8"));
  let password = process.env.PANEL_PASSWORD;
  if (!password) {
    const rl = createInterface({ input: process.stdin, output: process.stdout });
    password = (await rl.question("Contraseña del panel: ")).trim();
    rl.close();
  }
  const login = await fetch(`${panel}/api/login`, { method: "POST", headers: { "content-type": "application/json", "x-anuncios": "1", origin: panel }, body: JSON.stringify({ password }) });
  if (!login.ok) fail(`No se pudo entrar al panel: ${(await login.json().catch(() => ({}))).error ?? login.status}`);
  const headers = { cookie: login.headers.get("set-cookie").split(";")[0], "x-anuncios": "1", origin: panel };
  const state = await (await fetch(`${panel}/api/release`, { headers })).json();

  // ---- upload
  if (!process.env.BLOB_READ_WRITE_TOKEN) fail("Falta BLOB_READ_WRITE_TOKEN: corre «vercel env pull .env.local» en web/anuncios.");
  const pathname = `versiones/paquetes/${version}-${randomBytes(4).toString("hex")}.zip`;
  console.log(`Subiendo a ${prefix}${pathname}…`);
  await put(`${prefix}${pathname}`, createReadStream(zip), { access: "private", contentType: "application/zip", multipart: true, addRandomSuffix: false });

  // ---- sign and publish
  const targets = (option("--destinos", "") ?? "").split(",").map((t) => t.trim()).filter(Boolean);
  const pad = (n) => String(n).padStart(2, "0");
  const now = new Date();
  const offset = -now.getTimezoneOffset();
  const publishedAt = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}T${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}${offset >= 0 ? "+" : "-"}${pad(Math.floor(Math.abs(offset) / 60))}:${pad(Math.abs(offset) % 60)}`;
  const manifest = {
    release: Math.max(Math.floor(now.getTime() / 1000), (state.current?.release ?? 0) + 1),
    version,
    publishedAt,
    notes: option("--notas", ""),
    file: { pathname, size, sha256 },
    install: flag("--ahora") ? INSTALL_NOW : INSTALL_OFF_SHIFT,
    targets,
    paused: flag("--pausa"),
    allowDowngrade: flag("--permitir-bajar"),
  };
  const problems = validateManifest(manifest);
  if (problems.length) fail(problems.join(" "));
  const signed = await signEnvelope(RELEASE_FORMAT, manifest, privateKey, keyId);
  const posted = await fetch(`${panel}/api/release`, { method: "POST", headers: { ...headers, "content-type": "application/json" }, body: signed });
  const answer = await posted.json().catch(() => ({}));
  if (!posted.ok) fail(`El panel rechazó la publicación: ${answer.error ?? posted.status} ${(answer.details ?? []).join(" ")}`);
  console.log(`✓ Versión ${version} publicada (publicación ${manifest.release}).`);
  console.log(manifest.paused ? "  En pausa: nadie la instala hasta que la reanudes en el panel."
    : `  Las pantallas${targets.length ? ` (${targets.join(", ")})` : ""} la descargan en su siguiente reporte y la instalan ${manifest.install === INSTALL_NOW ? "al momento" : "fuera de turno"}.`);
}

main().catch((error) => fail(error.message));
