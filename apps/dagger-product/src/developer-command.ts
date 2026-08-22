import {
  createRustyDeveloperCommandClient,
  RUSTY_STANDARD_ADMIN_WIRE_SCHEMAS,
  type RustyDeveloperCommandAdapter,
  type RustyDeveloperCommandClient,
  type RustyDeveloperCommandValueSchema,
  type RustyDeveloperCommandWireSchema,
} from '@rusty-engine/application-host';

const DEVELOPER_COMMANDS_URL = '/api/dagger-product/developer-commands';

const opaque: RustyDeveloperCommandValueSchema = {
  kind: 'opaqueJson',
  maximumBytes: 65_536,
  maximumNodes: 2_048,
};

const inspectSchema: RustyDeveloperCommandWireSchema = {
  request: {
    kind: 'object',
    fields: { entity: { required: true, value: { kind: 'decimalU64' } } },
  },
  result: opaque,
  error: opaque,
};

const scenarioInteger: RustyDeveloperCommandValueSchema = {
  kind: 'integer',
  minimum: -1_000_000,
  maximum: 1_000_000,
};

const scenarioTrackSchema: RustyDeveloperCommandValueSchema = {
  kind: 'object',
  fields: {
    current: { required: true, value: scenarioInteger },
    maximum: { required: true, value: scenarioInteger },
  },
};

const scenarioResultSchema: RustyDeveloperCommandValueSchema = {
  kind: 'object',
  fields: {
    player: {
      required: true,
      value: {
        kind: 'object',
        fields: {
          health: { required: true, value: scenarioTrackSchema },
          stamina: { required: true, value: scenarioTrackSchema },
        },
      },
    },
    progression: {
      required: true,
      value: {
        kind: 'object',
        fields: {
          xp: { required: true, value: scenarioInteger },
          level: { required: true, value: scenarioInteger },
          awards: { required: true, value: { kind: 'integer', minimum: 0, maximum: 128 } },
        },
      },
    },
    latestCombat: {
      required: false,
      value: {
        kind: 'object',
        fields: {
          targetId: { required: true, value: { kind: 'decimalU64' } },
          outcome: { required: true, value: { kind: 'string', maximumBytes: 32 } },
          damage: { required: true, value: scenarioInteger },
          died: { required: true, value: { kind: 'boolean' } },
        },
      },
    },
    latestEncounter: {
      required: false,
      value: {
        kind: 'object',
        fields: {
          enemyId: { required: true, value: { kind: 'decimalU64' } },
          damage: { required: true, value: scenarioInteger },
          playerHealthBefore: { required: true, value: scenarioInteger },
          playerHealthAfter: { required: true, value: scenarioInteger },
        },
      },
    },
  },
};

const emptyScenarioSchema: RustyDeveloperCommandWireSchema = {
  request: { kind: 'object', fields: {} },
  result: scenarioResultSchema,
  error: opaque,
};

// Dagger's server is the discovery authority for its namespaced bindings.
// Supplying a second descriptor extension would falsely duplicate that public
// inventory; this product contributes only exact codecs for the discovered
// Dagger commands.
const daggerScenarioSchemas: Readonly<Record<string, RustyDeveloperCommandWireSchema>> = {
  'dagger.scenario.prepare': {
    request: {
      kind: 'object',
      fields: {
        target: {
          required: true,
          value: {
            kind: 'enum',
            values: ['rat', 'orc', 'bat-east', 'bat-west'],
          },
        },
      },
    },
    result: scenarioResultSchema,
    error: opaque,
  },
  'dagger.scenario.melee': boundedIntegerScenario('swings', 1, 8),
  'dagger.scenario.advance': boundedIntegerScenario('ticks', 1, 32),
  'dagger.scenario.progression': emptyScenarioSchema,
};

const standardSchemas: Readonly<Record<string, RustyDeveloperCommandWireSchema>> = {
  ...RUSTY_STANDARD_ADMIN_WIRE_SCHEMAS,
  'standard.inspect.entity': inspectSchema,
  'standard.inspect.mechanics': inspectSchema,
};

const browserAdapter: RustyDeveloperCommandAdapter = {
  discover: (signal) => requestJson(DEVELOPER_COMMANDS_URL, {
    method: 'GET',
    ...(signal === undefined ? {} : { signal }),
  }),
  execute: (request, signal) => requestJson(`${DEVELOPER_COMMANDS_URL}/execute`, {
    method: 'POST',
    ...(signal === undefined ? {} : { signal }),
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(request),
  }),
};

/** Creates the Engine client; this product file supplies only Dagger schemas and fetch binding. */
export function createDaggerDeveloperCommandClient(): RustyDeveloperCommandClient {
  return createRustyDeveloperCommandClient({
    adapter: browserAdapter,
    schemas: { ...standardSchemas, ...daggerScenarioSchemas },
  });
}

function boundedIntegerScenario(
  name: string,
  minimum: number,
  maximum: number,
): RustyDeveloperCommandWireSchema {
  return {
    request: {
      kind: 'object',
      fields: {
        [name]: { required: true, value: { kind: 'integer', minimum, maximum } },
      },
    },
    result: scenarioResultSchema,
    error: opaque,
  };
}

async function requestJson(
  input: RequestInfo | URL,
  init: RequestInit,
): Promise<unknown> {
  const response = await fetch(input, init);
  const body: unknown = await response.json().catch(() => undefined);
  if (!response.ok) {
    const message = typeof body === 'object' && body !== null && 'error' in body
      ? String(body.error)
      : `Dagger developer-command request failed with ${String(response.status)}`;
    throw new Error(message);
  }
  return body;
}
