// Usage: npm run hash-password -- "<password>"   (or no argument: a strong random password is generated)
// Prints the password and the ADMIN_PASSWORD_HASH value for Vercel (scrypt, the format lib/auth.ts expects).
import { randomBytes, scryptSync } from "node:crypto";

const given = process.argv[2];
const password = given ?? randomBytes(18).toString("base64url");
const N = 2 ** 15, r = 8, p = 1;
const salt = randomBytes(16);
const hash = scryptSync(password.normalize("NFC"), salt, 32, { N, r, p, maxmem: 256 * 1024 * 1024 });
if (!given) console.log(`Contraseña: ${password}`);
console.log(`ADMIN_PASSWORD_HASH=scrypt:${N}:${r}:${p}:${salt.toString("base64")}:${hash.toString("base64")}`);
console.log(`SESSION_SECRET=${randomBytes(32).toString("base64url")}`);
