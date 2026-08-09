# Renderer check migration

Rusty Dagger no longer owns a browser renderer package, HTML bootstrap, or
TypeScript renderer composition. The durable Privateer's Hold diagnostic is
the Rust binary `dagger-native-host`; it supplies Dagger-owned project meaning
through the public `rusty_engine` facade, while Engine privately owns its
Rust-to-webview renderer boundary.

From the repository root:

```bash
./scripts/verify-native-host.sh
cargo run -p dagger-studio-adapter --bin dagger-native-host
```

The first command proves exact checked texture resources, retained-frame and
view application, camera/resize/state/render readback, physical input routed
to authoritative Dagger player state, miss and hit picks, transactional mount
rejection, and disposal. The second launches the interactive native diagnostic.

This directory remains only as a migration pointer for older documentation and
bookmarks. Application or library code must not be added here.
