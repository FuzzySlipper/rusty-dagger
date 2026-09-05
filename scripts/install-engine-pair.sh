#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
metadata_file="$repo_root/Directory.Build.props"
pair_version=$(sed -n 's|.*<RustyEnginePackageVersion>\([^<]*\)</RustyEnginePackageVersion>.*|\1|p' "$metadata_file")
pair_source_revision=$(sed -n 's|.*<RustyEnginePairSourceRevision>\([^<]*\)</RustyEnginePairSourceRevision>.*|\1|p' "$metadata_file")
[[ -n "$pair_version" && -n "$pair_source_revision" ]] || {
  echo "Directory.Build.props must declare the Rusty Engine package version and pair source revision." >&2
  exit 2
}
pair_archive="rusty-engine-csharp-pair-${pair_version}-linux-x64.tar.gz"
pair_release_tag="csharp-sdk-v${pair_version}"
pair_url="https://github.com/FuzzySlipper/rusty-engine/releases/download/${pair_release_tag}/${pair_archive}"
pair_checksum=7c40d225759d604d028de9231682cc5260f8f031e946b3b98b49cd9ebccc427c
runtime_root="$repo_root/.runtime"
runtime_pack="$runtime_root/runtime-pack"
sdk_feed="$runtime_root/sdk-feed"
temporary_root=$(mktemp -d "${TMPDIR:-/tmp}/rusty-dagger-engine-pair.XXXXXX")
staged_root=""
staged_runtime=""
backup_runtime=""
runtime_installed=false

cleanup() {
  [[ -z "$temporary_root" ]] || rm -rf -- "$temporary_root"
  [[ -z "$staged_root" || ! -e "$staged_root" ]] || rm -rf -- "$staged_root"
  [[ -z "$staged_runtime" || ! -e "$staged_runtime" ]] || rm -rf -- "$staged_runtime"

  if [[ "$runtime_installed" == false && -n "$backup_runtime" && -e "$backup_runtime" && ! -e "$runtime_pack" ]]; then
    mv -- "$backup_runtime" "$runtime_pack"
  fi
}
trap cleanup EXIT INT TERM

archive_path="$temporary_root/$pair_archive"
checksum_path="$archive_path.sha256"
curl --fail --silent --show-error --location --retry 3 --retry-delay 1 --output "$archive_path" "$pair_url"
curl --fail --silent --show-error --location --retry 3 --retry-delay 1 --output "$checksum_path" "${pair_url}.sha256"

published_checksum=$(awk 'NF { print $1; exit }' "$checksum_path")
[[ "$published_checksum" == "$pair_checksum" ]] || {
  echo "Engine pair checksum file does not match the pinned release checksum." >&2
  exit 1
}

(
  cd "$temporary_root"
  sha256sum --check "$(basename "$checksum_path")"
)

tar -xzf "$archive_path" -C "$temporary_root"
mapfile -t extracted_roots < <(find "$temporary_root" -mindepth 1 -maxdepth 1 -type d -print)
[[ ${#extracted_roots[@]} -eq 1 ]] || {
  echo "Engine pair archive must extract exactly one root directory." >&2
  exit 1
}
pair_root=${extracted_roots[0]}

"$pair_root/verify-pair.sh" --directory "$pair_root"

manifest="$pair_root/pair-manifest.json"
jq -e \
  --arg package_version "$pair_version" \
  --arg source_revision "$pair_source_revision" \
  --arg package_path "sdk-feed/Rusty.Engine.${pair_version}.nupkg" \
  '.package.id == "Rusty.Engine" and
   .package.version == $package_version and
   .sourceRevision == $source_revision and
   .package.repositoryCommit == $source_revision and
   .package.path == $package_path and
   .runtime.path == "runtime-pack"' \
  "$manifest" >/dev/null || {
  echo "Engine pair manifest does not match this repository's pinned SDK identity." >&2
  exit 1
}

staged_root=$(mktemp -d "$repo_root/.runtime.install.XXXXXX")
rm -rf -- "$staged_root"
cp -a -- "$pair_root" "$staged_root"
"$staged_root/verify-pair.sh" --directory "$staged_root"

mkdir -p -- "$sdk_feed"
package_source="$staged_root/sdk-feed/Rusty.Engine.${pair_version}.nupkg"
package_target="$sdk_feed/Rusty.Engine.${pair_version}.nupkg"
if [[ -e "$package_target" ]]; then
  [[ $(sha256sum "$package_source" | awk '{print $1}') == $(sha256sum "$package_target" | awk '{print $1}') ]] || {
    echo "Refusing to overwrite a different pinned SDK package at $package_target." >&2
    exit 1
  }
else
  staged_package="$sdk_feed/.Rusty.Engine.${pair_version}.incoming.$$"
  cp -- "$package_source" "$staged_package"
  mv -- "$staged_package" "$package_target"
fi

staged_runtime=$(mktemp -d "$runtime_root/.runtime-pack.install.XXXXXX")
rmdir -- "$staged_runtime"
cp -a -- "$staged_root/runtime-pack" "$staged_runtime"

if [[ -e "$runtime_pack" ]]; then
  [[ -f "$runtime_pack/runtime-manifest.json" ]] || {
    echo "Refusing to replace a runtime-pack without runtime-manifest.json." >&2
    exit 1
  }
  current_revision=$(jq -r '.sourceRevision // "unknown"' "$runtime_pack/runtime-manifest.json")
  if [[ "$current_revision" == "$pair_source_revision" ]]; then
    rm -rf -- "$staged_runtime"
    staged_runtime=""
  else
    backup_runtime=$(mktemp -d "$runtime_root/.runtime-pack.previous.XXXXXX")
    rmdir -- "$backup_runtime"
    mv -- "$runtime_pack" "$backup_runtime"
  fi
fi

if [[ -n "$staged_runtime" ]]; then
  mv -- "$staged_runtime" "$runtime_pack"
  staged_runtime=""
fi
runtime_installed=true

cp -- "$staged_root/pair-manifest.json" "$runtime_root/.pair-manifest.incoming.$$"
mv -- "$runtime_root/.pair-manifest.incoming.$$" "$runtime_root/pair-manifest.json"
cp -- "$staged_root/verify-pair.sh" "$runtime_root/.verify-pair.incoming.$$"
chmod +x "$runtime_root/.verify-pair.incoming.$$"
mv -- "$runtime_root/.verify-pair.incoming.$$" "$runtime_root/verify-pair.sh"

echo "Installed verified Rusty Engine C# pair ${pair_version}; preserved $runtime_root persistence and retained the prior runtime pack when replaced."
