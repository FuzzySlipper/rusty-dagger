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
pair_checksum=b7624fa77d99171b7ab4e2b40a760e13939ce990f75606cfd1335fd6e98cce87
runtime_root="$repo_root/.runtime"
temporary_root=$(mktemp -d "${TMPDIR:-/tmp}/rusty-dagger-engine-pair.XXXXXX")
staged_root=""
backup_root=""
installed=false

cleanup() {
  [[ -z "$temporary_root" ]] || rm -rf -- "$temporary_root"
  [[ -z "$staged_root" || ! -e "$staged_root" ]] || rm -rf -- "$staged_root"

  if [[ -n "$backup_root" && -e "$backup_root" ]]; then
    if [[ "$installed" == true ]]; then
      rm -rf -- "$backup_root"
    elif [[ ! -e "$runtime_root" ]]; then
      mv -- "$backup_root" "$runtime_root"
    fi
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

if [[ -e "$runtime_root" ]]; then
  backup_root=$(mktemp -d "$repo_root/.runtime.previous.XXXXXX")
  rmdir -- "$backup_root"
  mv -- "$runtime_root" "$backup_root"
fi

mv -- "$staged_root" "$runtime_root"
staged_root=""
installed=true

echo "Installed verified Rusty Engine C# pair ${pair_version} at $runtime_root"
