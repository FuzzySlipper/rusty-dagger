import { decodeDaggerExpr } from "../dist/authoring/expressions.js";

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

// The nested pow children are complete composed trees, not merely objects.
decodeDaggerExpr(product("pow-milli", {
  base: product("dice", { id: "rat.health", min: 1, max: 16 }),
  exponent: literal(2),
}));
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
  base: { op: "literal", value: 1, extra: true },
  exponent: literal(2),
}), "malformed nested composed tree");

console.log("Dagger composed expression adapter check passed");
