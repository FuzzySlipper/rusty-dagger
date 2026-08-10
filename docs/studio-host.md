# Rusty Dagger Studio integration

Rusty Studio is an Engine-hosted product. Rusty Dagger does not install,
import, build, bundle, copy, or serve Studio or its renderer implementation.
This repository's complete ordinary Studio boundary is:

- committed project data under `content/projects/`;
- the trusted root-local `.rusty-studio.json` bootstrap; and
- `dagger-studio-adapter`, the project-owned Rust protocol authority.

The bootstrap tells the generic Engine host how to start the adapter. The
adapter admits Dagger project data, projects canonical readback and exact
resources, and fails unsupported mutations closed. Engine owns the Studio
service, browser application, host-file/settings transport, renderer, and
browser-level integration tests without acquiring Dagger project or gameplay
meaning.

## Normal interactive entrypoint

On a machine with the persistent Engine service installed, confirm its health:

```sh
systemctl --user status rusty-studio.service
curl http://127.0.0.1:4310/health
```

Then open `http://127.0.0.1:4310/` and select this repository root plus
`content/projects/privateers-hold.project.json`. Studio reads the root-local
bootstrap and starts the adapter from the repository working directory.

Service installation, update, rollback, binding, and health are Engine
operator concerns documented in
`../rusty-engine/docs/topics/studio-service.md`. Downstream work must not
update the service or fetch, pull, reset, clean, checkout, or otherwise mutate
the sibling Engine checkout as an incidental setup step.

The persistent service currently has one process-wide active project. Use it
for one interactive authoring session at a time. Concurrent automation needs
an isolated Engine host on a unique loopback port and a separate settings
root; separate project copies or explicit coordination are also required for
concurrent mutation.

## Dagger-owned checks

The focused checks in this repository stop at its actual ownership boundary:

```sh
cargo build -p dagger-studio-adapter
python3 scripts/check-adapter.py
```

`scripts/check-adapter.py` proves local stdio open/read/close behavior.
`RUSTY_STUDIO_ADAPTER` remains an explicit diagnostic override; normal
regeneration builds and checks the local adapter. Engine owns service and
real-browser Studio certification.

The connected Dagger product and the fixed native renderer diagnostic are
separate from Studio:

```sh
./scripts/check-dagger-lab-browser.sh
./scripts/verify-native-host.sh
```

Those gates prove the Dagger gameplay/application-host product and Engine's
public native renderer facade respectively; neither makes Dagger a Studio or
renderer implementation owner.
