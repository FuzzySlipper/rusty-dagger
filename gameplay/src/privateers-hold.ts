import {
  action,
  evidenceAtLeast,
  forIntentTarget,
  item,
  operation,
  packageEnvelope,
  rule,
  sequence,
  when,
  type DaggerGameplayPayload,
} from "./authoring.js";

const payload = {
  schemaVersion: 1,
  actions: [
    action(
      "ember-lance",
      ["spell", "fire"],
      when(
        evidenceAtLeast("spell-hit", 50),
        sequence(
          operation({ kind: "spendMagicka", amount: 5 }),
          forIntentTarget(operation({ kind: "damage", amount: 12 })),
        ),
      ),
    ),
  ],
  items: [
    item("ruby-ward", { kind: "reduceDamage", amount: 3 }),
  ],
  rules: [rule("silence", "spell", "silenced")],
} as const satisfies DaggerGameplayPayload;

export const daggerGameplayPackage = packageEnvelope(payload);
