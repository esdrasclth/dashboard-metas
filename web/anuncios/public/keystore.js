// Keeps the signing key in this browser (IndexedDB) as a NON-extractable CryptoKey: the page can use it to sign
// but nothing (not even this code) can read the key material back. «Olvidar» deletes it.

const DB = "dashboard-metas-anuncios";
const STORE = "claves";
const ENTRY = "firma";

function open() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DB, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

async function run(mode, action) {
  const db = await open();
  try {
    return await new Promise((resolve, reject) => {
      const tx = db.transaction(STORE, mode);
      const request = action(tx.objectStore(STORE));
      tx.oncomplete = () => resolve(request?.result);
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error);
    });
  } finally {
    db.close();
  }
}

/** @returns {Promise<{privateKey: CryptoKey, keyId: string, savedAt: string} | null>} */
export async function loadKey() {
  try {
    return (await run("readonly", (store) => store.get(ENTRY))) ?? null;
  } catch {
    return null;
  }
}

export async function saveKey(privateKey, keyId) {
  await run("readwrite", (store) => store.put({ privateKey, keyId, savedAt: new Date().toISOString() }, ENTRY));
}

export async function forgetKey() {
  await run("readwrite", (store) => store.delete(ENTRY));
}
