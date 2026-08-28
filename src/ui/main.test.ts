import { isHud } from './main.js';

const validHud = {
  resources: [{ id: 'health', label: 'Health', current: 85, maximum: 85 }],
  lastOutcome: 'Exploring',
};

assert(isHud(validHud), 'expected a complete HUD projection to be accepted');
assert(!isHud({ ...validHud, resources: [{ ...validHud.resources[0], current: Number.NaN }] }), 'expected NaN resource values to be rejected');
assert(!isHud({ ...validHud, resources: [{ ...validHud.resources[0], maximum: Number.POSITIVE_INFINITY }] }), 'expected infinite resource values to be rejected');

function assert(condition: boolean, message: string): asserts condition {
  if (!condition) throw new Error(message);
}
