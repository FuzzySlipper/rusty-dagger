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

// Generic numeric nodes are Engine-generated and still compose with the
// Dagger-owned dynamic product leaves.
embedDaggerExpr(decodeDaggerExpr({
  op: "fixedPower",
  base: literal(1040),
  exponent: { op: "input", input: { kind: "boundedRoll", role: "actor", id: "level", minimum: 0, maximum: 64 } },
  scale: 1000,
}));
rejects(product("dice", { id: "rat.health", min: 1, max: 6 }), "retired dice product leaf");
rejects(product("pow-milli", { base: 1040, exponentRoll: "level" }), "retired pow-milli product leaf");
rejects({
  ...product("equipped-weapon-dice", { id: "rat.health", subject: "actor" }),
  payload: { kind: "struck-armor", value: { id: "rat.health", min: 1, max: 6 } },
}, "payload kind differs from enclosing kind");
rejects({
  ...product("equipped-weapon-dice", { id: "rat.health", subject: "actor" }),
  source: "other-product",
}, "non-Dagger product provenance");
rejects(product("equipped-weapon-dice", {
  id: "rat.health", subject: "actor", extra: true,
}), "unknown product leaf field");
rejects({
  op: "input",
  input: { kind: "boundedRoll", role: "actor", id: "1starts-with-digit", minimum: 1, maximum: 6 },
}, "id must start with a lowercase letter");
rejects({
  op: "input",
  input: { kind: "boundedRoll", role: "actor", id: "rat.health", minimum: 7, maximum: 6 },
}, "bounded range is inverted");
rejects({
  op: "add",
  left: { op: "input", input: { kind: "boundedRoll", role: "actor", id: "same-roll", minimum: 1, maximum: 6 } },
  right: { op: "input", input: { kind: "boundedRoll", role: "actor", id: "same-roll", minimum: 2, maximum: 6 } },
}, "conflicting bounded roll descriptors share one identity");
rejects({
  op: "fixedPower", base: literal(1040), exponent: literal(1), scale: 0,
}, "fixed power scale is invalid");

console.log("Dagger composed expression adapter check passed");
