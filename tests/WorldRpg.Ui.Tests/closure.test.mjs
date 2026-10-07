import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readFileSync, readdirSync } from 'node:fs';
import { basename, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

// The DOM and the ruleset meet at two wires: the semantic actions the DOM sends and the HUD
// projection the ruleset publishes. These checks read both sides as written - the TypeScript
// through its own checker, the ruleset's action table and projection keys from its source - so a
// wire one side renames, drops or never consumes fails here instead of becoming a dead button or
// a field nobody shows.
const repository = fileURLToPath(new URL('../../', import.meta.url));
const ui = join(repository, 'src/ui');
const ruleset = join(repository, 'src/WorldRpg.Rulesets.Daggerfall');

const config = ts.getParsedCommandLineOfConfigFile(join(ui, 'tsconfig.json'), {}, {
  ...ts.sys,
  onUnRecoverableConfigFileDiagnostic: diagnostic => { throw new Error(ts.flattenDiagnosticMessageText(diagnostic.messageText, '\n')); },
});
const program = ts.createProgram(config.fileNames, config.options);
// The Engine's UI types are copied in by the Host build. Without them the Engine-typed values read
// as `any`, and the closure facts below report a different, wrong set of read fields.
const engineTypes = join(ui, 'engine-types/rusty-engine-product-ui.d.ts');
assert.ok(program.getSourceFile(engineTypes),
  `${engineTypes} is missing: the Host build copies it from the pinned Engine SDK, so build src/WorldRpg.Host before these tests`);
const checker = program.getTypeChecker();
const domSources = program.getSourceFiles().filter(file => file.fileName.startsWith(ui) && !file.fileName.endsWith('.d.ts'));
assert.ok(domSources.some(file => basename(file.fileName) === 'main.ts'), 'the UI program does not include main.ts');

const where = (file, node) => `${basename(file.fileName)}:${file.getLineAndCharacterOfPosition(node.getStart(file)).line + 1}`;
const visitAll = visit => { for (const file of domSources) { const walk = node => { visit(file, node); ts.forEachChild(node, walk); }; walk(file); } };
const rulesetSources = (() => {
  const files = [];
  const walk = directory => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) { if (entry.name !== 'obj' && entry.name !== 'bin') walk(path); } else if (entry.name.endsWith('.cs')) files.push(path);
    }
  };
  walk(ruleset);
  return files.map(path => readFileSync(path, 'utf8'));
})();

/** Every wire the ruleset's action table admits, read from the table itself. */
function rulesetWires() {
  const source = readFileSync(join(ruleset, 'Presentation/DaggerfallUiAction.cs'), 'utf8');
  const table = /DaggerfallUiActionRule> Rules =\s*\[(?<rules>[\s\S]*?)\n\s*\];/.exec(source)?.groups?.rules;
  assert.ok(table, 'DaggerfallUiAction.Rules was not found');
  const constant = name => {
    const field = name.split('.').at(-1);
    const values = rulesetSources.flatMap(text => [...text.matchAll(new RegExp(`const string ${field} = "([^"]+)"`, 'g'))].map(match => match[1]));
    assert.equal(values.length, 1, `the action wire constant '${name}' resolves to ${values.length} declarations`);
    return values[0];
  };
  const entries = [...table.matchAll(/new\(DaggerfallUiActionKind\.(\w+),\s*(?:"([^"]+)"|([\w.]+))/g)]
    .map(match => ({ kind: match[1], wire: match[2] ?? constant(match[3]) }));
  // Every rule is read: a rule the pattern skipped would be an admitted wire this check never sees.
  assert.equal(entries.length, [...table.matchAll(/new\(DaggerfallUiActionKind\./g)].length);
  const wires = new Set(entries.map(entry => entry.wire));
  assert.equal(wires.size, entries.length, 'two rules share one wire');
  return wires;
}

/** The string literals a type stands for, or null when any part of it is wider than a literal. */
function literalValues(type) {
  const parts = (type.isUnion() ? type.types : [type]).filter(part => !(part.flags & (ts.TypeFlags.Undefined | ts.TypeFlags.Null)));
  return parts.every(part => part.isStringLiteral()) ? parts.map(part => part.value) : null;
}

/**
 * The actions the DOM sends, as the checker resolves them: the literal type of every `action` an
 * object literal carries, and the literal argument of every helper whose first parameter is the
 * action and whose body sends the UI payload contract.
 */
function domSentActions() {
  const sent = new Map();
  const add = (value, at) => sent.set(value, [...(sent.get(value) ?? []), at]);
  visitAll((file, node) => {
    if (ts.isObjectLiteralExpression(node.parent ?? node) && (ts.isPropertyAssignment(node) || ts.isShorthandPropertyAssignment(node))
      && node.name.getText(file) === 'action') {
      const type = ts.isPropertyAssignment(node)
        ? checker.getTypeAtLocation(node.initializer)
        : checker.getTypeOfSymbolAtLocation(checker.getShorthandAssignmentValueSymbol(node), node.name);
      for (const value of literalValues(type) ?? []) add(value, where(file, node));
    }
    if (ts.isCallExpression(node) && node.arguments.length > 0 && ts.isStringLiteralLike(node.arguments[0])) {
      const declaration = checker.getResolvedSignature(node)?.getDeclaration();
      const parameter = declaration?.parameters?.[0];
      if (parameter && parameter.name.getText() === 'action' && declaration.body?.getText().includes('UI_ACTION_CONTRACT'))
        add(node.arguments[0].text, where(file, node));
    }
  });
  return sent;
}

/** Every string literal written in the DOM sources, wherever it appears. */
function domLiterals() {
  const literals = new Set();
  visitAll((_, node) => { if (ts.isStringLiteralLike(node)) literals.add(node.text); });
  return literals;
}

/**
 * Admitted wires no DOM control sends, each with the reason it is admitted anyway. A wire the DOM
 * starts sending must leave this list, and a wire the ruleset drops must leave it too.
 */
const NON_DOM_ACTIONS = new Map([
  ['attack', 'The player attacks through the mapped "attack" input intent; the payload form is the direct-UI path the controls arbitration tests drive against interaction, and no DOM control offers it.'],
]);

test('every action the DOM sends is one the ruleset admits, and every admitted action is sent or named', () => {
  const wires = rulesetWires();
  const sent = domSentActions();
  assert.ok(sent.size > 50, `only ${sent.size} sent actions were resolved from the DOM`);
  const unknown = [...sent].filter(([value]) => !wires.has(value)).map(([value, at]) => `${value} (${at.join(', ')})`);
  assert.deepEqual(unknown, [], 'the DOM sends actions the ruleset does not admit');

  // Some wires reach the DOM's senders through a data attribute or a projected value rather than a
  // typed literal, so a wire counts as sent when the DOM writes it anywhere.
  const literals = domLiterals();
  const unsent = [...wires].filter(wire => !sent.has(wire) && !literals.has(wire) && !NON_DOM_ACTIONS.has(wire)).sort();
  assert.deepEqual(unsent, [], 'the ruleset admits actions no DOM control sends and none is named as non-DOM');
  for (const [wire, reason] of NON_DOM_ACTIONS) {
    assert.ok(wires.has(wire), `'${wire}' is named as a non-DOM action but the ruleset no longer admits it`);
    assert.ok(!sent.has(wire) && !literals.has(wire), `'${wire}' is named as a non-DOM action but the DOM sends it`);
    assert.ok(reason.length > 0);
  }

  // The payload contract the actions travel under is one name on both sides.
  const csharp = /Contract => "([^"]+)"u8/.exec(readFileSync(join(ruleset, 'Presentation/DaggerfallUiAction.cs'), 'utf8'))?.[1];
  const dom = /const UI_ACTION_CONTRACT = '([^']+)'/.exec(readFileSync(join(ui, 'main.ts'), 'utf8'))?.[1];
  assert.ok(csharp);
  assert.equal(dom, csharp);
});

const UNREAD_INTERNAL = 'An owner identity or input the ruleset already turned into the label, verdict or worded text the DOM shows.';
const UNREAD_WORDED_TIME = 'The ruleset words this time for the player in a field the DOM shows; the structured value stays for consumers that format their own.';
const UNREAD_CREATION = 'Character-creation draft internals: the draft shows final values and grant labels, while rolls, bonuses and grant identities stay the ruleset\'s draft state.';
const UNREAD_TRAVEL_INPUTS = 'The quote shows the ruleset-worded journey, its costs and canAfford verdict; the raw measures and the option inputs it was computed from stay for diagnostics.';

/**
 * HUD fields the DOM does not read, each with the reason that is deliberate. A field the DOM starts
 * reading, or one the projection stops publishing, must leave this list.
 */
const UNREAD_HUD_FIELDS = new Map([
  ['hud.detectors[].contacts[].items[].definition', UNREAD_INTERNAL],
  ['hud.calendar.dayName', UNREAD_WORDED_TIME],
  ['hud.calendar.day', UNREAD_WORDED_TIME],
  ['hud.calendar.monthName', UNREAD_WORDED_TIME],
  ['hud.calendar.year', UNREAD_WORDED_TIME],
  ['hud.calendar.hour', UNREAD_WORDED_TIME],
  ['hud.calendar.minute', UNREAD_WORDED_TIME],
  ['hud.effects[].source', 'The effect detail line already names the item an effect comes from.'],
  ['hud.effects[].remainingSeconds', UNREAD_WORDED_TIME],
  ['hud.effects[].remaining', 'The effect detail line already carries the worded remaining time.'],
  ['hud.cinematic.source', 'The Engine plays the video; the DOM offers only the skip, and the source names the playing film for diagnostics.'],
  ['hud.inventory.equipmentChange.rightHandDelayMilliseconds', 'Hand timing belongs to the Engine-rendered viewmodel; the DOM shows only the completed equip cue.'],
  ['hud.inventory.equipmentChange.leftHandDelayMilliseconds', 'Hand timing belongs to the Engine-rendered viewmodel; the DOM shows only the completed equip cue.'],
  ['hud.inventory.bank.loan.dueMinute', UNREAD_WORDED_TIME],
  ['hud.character.creation.background.attributes[].rolled', UNREAD_CREATION],
  ['hud.character.creation.background.skills[].rolled', UNREAD_CREATION],
  ['hud.character.creation.background.skills[].biographyBonus', UNREAD_CREATION],
  ['hud.character.creation.background.startingGrants[].itemId', UNREAD_CREATION],
  ['hud.character.creation.background.startingGrants[].templateIndex', UNREAD_CREATION],
  ['hud.character.creation.background.startingGrants[].sourceEffect', UNREAD_CREATION],
  ['hud.uiArt.revision', 'Adopted by art.ts through its own guard on the art block rather than through the HUD type.'],
  ['hud.uiArt.images', 'Adopted by art.ts through its own guard on the art block rather than through the HUD type.'],
  ['hud.activation.message', 'The session sets the same text as lastOutcome, which the HUD shows.'],
  ['hud.activation.applied', 'The DOM shows the activation outcome text and does not branch on whether it applied.'],
  ['hud.activation.dialogue.questContacts[].instance', 'The DOM marks a quest contact by the list being non-empty; instance and symbol are quest-owner identities.'],
  ['hud.activation.dialogue.questContacts[].symbol', 'The DOM marks a quest contact by the list being non-empty; instance and symbol are quest-owner identities.'],
  ['hud.activation.dialogue.merchant.result', 'The session projects the merchant without a result; trade outcomes reach the player as lastOutcome.'],
  ['hud.activation.dialogue.merchant.stock[].definition', UNREAD_INTERNAL],
  ['hud.activation.dialogue.merchant.repairs[].durableItemId', UNREAD_INTERNAL],
  ['hud.activation.dialogue.merchant.repairs[].dueMinute', UNREAD_INTERNAL],
  ['hud.activation.dialogue.training.providerFaction', UNREAD_INTERNAL],
  ['hud.activation.dialogue.training.membershipFaction', UNREAD_INTERNAL],
  ['hud.activation.dialogue.training.cooldownReadySecond', UNREAD_INTERNAL],
  ['hud.property.storage.items[].definition', UNREAD_INTERNAL],
  ['hud.travel.quote.minutes', UNREAD_TRAVEL_INPUTS],
  ['hud.travel.quote.distance', UNREAD_TRAVEL_INPUTS],
  ['hud.travel.quote.oceanPixels', UNREAD_TRAVEL_INPUTS],
  ['hud.travel.quote.options.hasHorse', UNREAD_TRAVEL_INPUTS],
  ['hud.travel.quote.options.hasCart', UNREAD_TRAVEL_INPUTS],
  ['hud.travel.quote.options.hasShip', UNREAD_TRAVEL_INPUTS],
  ['hud.travel.quote.options.availableGold', UNREAD_TRAVEL_INPUTS],
  ['hud.travel.quote.options.availableGoldPieces', UNREAD_TRAVEL_INPUTS],
  ['hud.travel.lastResult.paidGold', 'The arrival message words what the journey cost.'],
  ['hud.travel.lastResult.elapsedSeconds', 'The arrival message words how long the journey took.'],
  ['hud.quests.deliveries[].diagnostics', 'Shown with every quest message\'s diagnostics through the combined message list, whose element type the checker reduces to another member.'],
  ['hud.quests.journal.active[].entries[].diagnostics', 'Shown with every quest message\'s diagnostics through the combined message list, whose element type the checker reduces to another member.'],
  ['hud.quests.journal.active[].entries[].instance', 'A journal entry is drawn inside its quest group, which already names the instance.'],
  ['hud.quests.journal.active[].entries[].message', UNREAD_INTERNAL],
  ['hud.view.interaction', 'The DOM follows the product mode; interaction names the camera aim state for the Engine look owner.'],
  ['hud.slots[].owner', 'Rows arrive in the owner-published order and are keyed by id; owner names the publisher.'],
  ['hud.slots[].order', 'Rows arrive in the owner-published order and are keyed by id; owner names the publisher.'],
]);

/** The interface the DOM reads every snapshot through. */
function hudType() {
  const main = domSources.find(file => basename(file.fileName) === 'main.ts');
  const declaration = main.statements.find(statement => ts.isInterfaceDeclaration(statement) && statement.name.text === 'DaggerHud');
  assert.ok(declaration, 'the DOM no longer declares DaggerHud');
  return checker.getTypeAtLocation(declaration.name);
}

/** The declarations of every property the DOM reads, by access, destructuring, keyed access or an `in` check. */
function domReads() {
  const declarations = new Set();
  const names = new Set();
  const mark = symbol => {
    if (!symbol) return;
    for (const root of [symbol, ...checker.getRootSymbols(symbol)]) for (const declaration of root.declarations ?? []) declarations.add(declaration);
  };
  visitAll((file, node) => {
    if (ts.isPropertyAccessExpression(node)) {
      mark(checker.getSymbolAtLocation(node.name));
      // A guard reading an unknown value names the field without a declaration to resolve.
      if (checker.getTypeAtLocation(node.expression).flags & (ts.TypeFlags.Any | ts.TypeFlags.Unknown)) names.add(node.name.text);
    }
    if (ts.isElementAccessExpression(node)) {
      const owner = checker.getNonNullableType(checker.getTypeAtLocation(node.expression));
      for (const key of literalValues(checker.getTypeAtLocation(node.argumentExpression)) ?? []) mark(owner.getProperty(key));
    }
    if (ts.isBindingElement(node) && ts.isObjectBindingPattern(node.parent))
      mark(checker.getTypeAtLocation(node.parent).getProperty((node.propertyName ?? node.name).getText(file)));
    if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.InKeyword && ts.isStringLiteralLike(node.left)) names.add(node.left.text);
  });
  return property => names.has(property.name) || (property.declarations ?? []).some(declaration => declarations.has(declaration));
}

/** Calls back with every property reachable from a type, by its path from the HUD root. */
function eachProperty(type, path, visit, seen = new Set()) {
  for (const part of type.isUnion() ? type.types : [type]) {
    if (part.flags & (ts.TypeFlags.Null | ts.TypeFlags.Undefined)) continue;
    if (checker.isArrayType(part)) { eachProperty(checker.getTypeArguments(part)[0], `${path}[]`, visit, seen); continue; }
    if (!(part.flags & ts.TypeFlags.Object) || seen.has(part)) continue;
    seen.add(part);
    for (const property of checker.getPropertiesOfType(part)) {
      visit(property, `${path}.${property.name}`);
      eachProperty(checker.getTypeOfSymbol(property), `${path}.${property.name}`, visit, seen);
    }
  }
}

/** Walks a published value against the type that reads it, reporting keys the type does not declare. */
function undeclaredPublished(value, type, path, found) {
  if (value === null || typeof value !== 'object') return;
  const parts = (type.isUnion() ? type.types : [type]).filter(part => !(part.flags & (ts.TypeFlags.Null | ts.TypeFlags.Undefined)));
  if (Array.isArray(value)) {
    const element = parts.filter(part => checker.isArrayType(part)).map(part => checker.getTypeArguments(part)[0])[0];
    assert.ok(element, `${path} is published as an array the DOM does not type as one`);
    for (const item of value) undeclaredPublished(item, element, `${path}[]`, found);
    return;
  }
  const objects = parts.filter(part => part.flags & ts.TypeFlags.Object && !checker.isArrayType(part));
  assert.ok(objects.length > 0, `${path} is published as an object the DOM types as ${parts.map(part => checker.typeToString(part)).join(' | ')}`);
  const properties = new Map(objects.flatMap(part => checker.getPropertiesOfType(part)).map(property => [property.name, property]));
  const open = objects.some(part => checker.getIndexInfosOfType(part).length > 0);
  for (const [key, child] of Object.entries(value)) {
    const property = properties.get(key);
    if (property) undeclaredPublished(child, checker.getTypeOfSymbol(property), `${path}.${key}`, found);
    else if (!open) found.push(`${path}.${key}`);
  }
  for (const [name, property] of properties)
    if (!(name in value) && !(property.flags & ts.SymbolFlags.Optional)) found.push(`${path}.${name} (required by the DOM, absent from the snapshot)`);
}

test('every published HUD field is one the DOM reads or names as deliberately unread', () => {
  const type = hudType();
  // The published snapshot is the one HudSnapshotContractTests regenerates from a real session.
  const snapshot = JSON.parse(readFileSync(join(repository, 'tests/WorldRpg.Ui.Tests/fixtures/hud-snapshot.json'), 'utf8'));
  const undeclared = [];
  undeclaredPublished(snapshot, type, 'hud', undeclared);
  assert.deepEqual(undeclared, [], 'the projection publishes fields the DOM does not declare');

  // Every field the DOM declares is one the ruleset names somewhere it builds a projection, so the
  // DOM cannot wait on a field no owner publishes.
  const published = new Set(rulesetSources.flatMap(text => [...text.matchAll(/\(\s*"([A-Za-z][A-Za-z0-9]*)"\s*,/g)].map(match => match[1])));
  const unpublished = [];
  const isRead = domReads();
  const unread = [];
  eachProperty(type, 'hud', (property, path) => {
    if (!published.has(property.name)) unpublished.push(path);
    if (!isRead(property)) unread.push(path);
  });
  assert.deepEqual(unpublished, [], 'the DOM declares HUD fields no ruleset projection names');
  assert.deepEqual(unread.sort(), [...UNREAD_HUD_FIELDS.keys()].sort(), 'the DOM leaves HUD fields unread that are not named as deliberate, or names one it now reads');
  for (const reason of UNREAD_HUD_FIELDS.values()) assert.ok(reason.length > 0);
});
