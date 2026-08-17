import { mkdir, readdir, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

// Materializes every package entry in gameplay/src/packages/ (compiled to
// dist/packages/) into data/gameplay/<domain>-<package>.package.json.
// Output is deterministic: same sources, same bytes, drift-checked by
// `pnpm gameplay:check`.

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const packagesDirectory = resolve(scriptDirectory, "../dist/packages");
const outputDirectory = resolve(scriptDirectory, "../../data/gameplay");

const entries = (await readdir(packagesDirectory))
  .filter((entry) => entry.endsWith(".js"))
  .sort();

await mkdir(outputDirectory, { recursive: true });
for (const entry of entries) {
  const module = await import(pathToFileURL(resolve(packagesDirectory, entry)).href);
  const gameplayPackage = module.gameplayPackage;
  if (gameplayPackage === undefined) {
    throw new Error(`${entry} does not export gameplayPackage`);
  }
  const name = `${gameplayPackage.domain}-${gameplayPackage.package}.package.json`;
  const output = resolve(outputDirectory, name);
  await writeFile(output, `${JSON.stringify(gameplayPackage, null, 2)}\n`, "utf8");
  console.log(`materialized ${name}`);
}
