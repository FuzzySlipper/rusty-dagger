import { decodeDaggerExpr, embedDaggerExpr } from "../dist/authoring/expressions.js";

const literal = (value) => ({ op: "literal", value });
const product = (kind, value) => ({
  op: "product",
  kind,
  payload: { kind, value },
  subject: "dagger",
  source: "dagger",
});

const rejects = (value, label) => {
  try {
    decodeDaggerExpr(value);
  } catch {
    return;
  }
  throw new Error(`expected strict Dagger expression rejection: ${label}`);
};

// The static product leaf carries only explicit Dagger inputs. Generic
// arithmetic remains the Engine-generated tree wrapped by its aggregate helper.
embedDaggerExpr(decodeDaggerExpr(product("pow-milli", {
  base: 1040,
  exponentRoll: "level",
})));
rejects(product("dice", { id: "bad id", min: 1, max: 6 }), "noncanonical id");
rejects({
  ...product("dice", { id: "rat.health", min: 1, max: 6 }),
  payload: { kind: "struck-armor", value: { id: "rat.health", min: 1, max: 6 } },
}, "payload kind differs from enclosing kind");
rejects({
  ...product("dice", { id: "rat.health", min: 1, max: 6 }),
  source: "other-product",
}, "non-Dagger product provenance");
rejects(product("pow-milli", {
  base: 1040, exponentRoll: "level", extra: true,
}), "unknown product leaf field");
rejects(product("dice", { id: "1starts-with-digit", min: 1, max: 6 }), "id must start with a lowercase letter");
rejects(product("dice", { id: "ends!bad", min: 1, max: 6 }), "id must use upstream allowed characters");
rejects(product("pow-milli", { base: 9_007_199_254_740_992, exponentRoll: "level" }), "unsafe product integer");

console.log("Dagger composed expression adapter check passed");
