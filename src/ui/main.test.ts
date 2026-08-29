import { isHud } from './main.js';

const validHud = {
  resources: [{ id: 'health', label: 'Health', current: 85, maximum: 85 }],
  lastOutcome: 'Exploring',
  composition: {
    bundle: 'daggerfall.privateers-hold',
    ruleset: 'daggerfall',
    contentPacks: ['daggerfall.base', 'daggerfall.privateers-hold'],
    tuning: 'daggerfall.defaults',
    fingerprint: 'a'.repeat(64),
    contentFingerprint: 'b'.repeat(64),
    tuningFingerprint: 'c'.repeat(64),
  },
};

assert(isHud(validHud), 'expected a complete HUD projection to be accepted');
assert(!isHud({ ...validHud, resources: [{ ...validHud.resources[0], current: Number.NaN }] }), 'expected NaN resource values to be rejected');
assert(!isHud({ ...validHud, resources: [{ ...validHud.resources[0], maximum: Number.POSITIVE_INFINITY }] }), 'expected infinite resource values to be rejected');
assert(!isHud({ ...validHud, composition: { ...validHud.composition, contentPacks: ['daggerfall.base', 3] } }), 'expected non-string content-pack identities to be rejected');
assert(!isHud({ ...validHud, composition: { ...validHud.composition, fingerprint: 'not-a-fingerprint' } }), 'expected malformed fingerprints to be rejected');

function assert(condition: boolean, message: string): asserts condition {
  if (!condition) throw new Error(message);
}
