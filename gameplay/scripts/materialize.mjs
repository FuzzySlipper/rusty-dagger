import { mkdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import { daggerGameplayPackage } from "../dist/privateers-hold.js";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const output = resolve(
  scriptDirectory,
  "../../data/gameplay/dagger-core.package.json",
);

await mkdir(dirname(output), { recursive: true });
await writeFile(output, `${JSON.stringify(daggerGameplayPackage, null, 2)}\n`, "utf8");
