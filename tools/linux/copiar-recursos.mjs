// Copies the Win32 resources (version, icon, manifest) of the compiled assembly into the exe host, as the .NET SDK
// does on Windows. On Linux the SDK skips that step without warning: the exe would report the host's version
// (10.0.x) instead of the app's and the screens would reject the update. Called by apphost.targets.
//
//   node copiar-recursos.mjs <DashboardMetas.dll> <singlefilehost.exe>

import { readFileSync, writeFileSync } from "node:fs";
import * as PE from "pe-library";
import * as ResEdit from "resedit";

const [assemblyPath, hostPath] = process.argv.slice(2);
const read = (file) => PE.NtExecutable.from(readFileSync(file), { ignoreCert: true });

const assembly = ResEdit.NtExecutableResource.from(read(assemblyPath));
const host = read(hostPath);
const resources = ResEdit.NtExecutableResource.from(host);
const same = (a, b) => a.type === b.type && a.id === b.id && a.lang === b.lang;
for (const entry of assembly.entries) {
  resources.entries = resources.entries.filter((e) => !same(e, entry));
  resources.entries.push(entry);
}
resources.outputResource(host);
writeFileSync(hostPath, Buffer.from(host.generate()));

const fixed = ResEdit.Resource.VersionInfo.fromEntries(resources.entries)[0]?.fixedInfo;
const version = fixed ? `${fixed.fileVersionMS >>> 16}.${fixed.fileVersionMS & 0xffff}.${fixed.fileVersionLS >>> 16}.${fixed.fileVersionLS & 0xffff}` : "?";
console.log(`Recursos copiados a ${hostPath} (versión ${version})`);
