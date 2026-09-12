#!/usr/bin/env bash
# Move this repository to the newest published Rusty Engine C# pair.
#
# The pin stays explicit in Directory.Build.props so taking a new Engine is a
# deliberate act, but moving it is one command: resolve the newest csharp-sdk
# release, rewrite both identities, and install the verified pair.
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
engine_repo="FuzzySlipper/rusty-engine"
tag_prefix="csharp-sdk-v"

pin_only=false
check_only=false
for argument in "$@"; do
  case "$argument" in
    --pin-only) pin_only=true ;;
    --check) check_only=true ;;
    -h|--help)
      echo "usage: scripts/update-engine-pin.sh [--check | --pin-only]"
      echo "  no arguments  rewrite Directory.Build.props and install the newest pair"
      echo "  --check       report the newest published pair without changing anything"
      echo "  --pin-only    rewrite Directory.Build.props without installing"
      exit 0
      ;;
    *)
      echo "Unknown argument: $argument (supported: --check, --pin-only)" >&2
      exit 2
      ;;
  esac
done

command -v gh >/dev/null || {
  echo "Resolving the newest release needs the GitHub CLI (gh)." >&2
  exit 1
}
command -v jq >/dev/null || {
  echo "This script needs jq." >&2
  exit 1
}

tag=$(gh release list -R "$engine_repo" --limit 100 --json tagName,createdAt \
  --jq "[.[] | select(.tagName | startswith(\"$tag_prefix\"))] | sort_by(.createdAt) | reverse | .[0].tagName")
[[ -n "$tag" && "$tag" != "null" ]] || {
  echo "No ${tag_prefix}* release found in $engine_repo." >&2
  exit 1
}
version=${tag#"$tag_prefix"}

# A release tag may be annotated, so dereference it to the commit it names and
# record the Engine source the pair was actually built from.
target=$(gh api "repos/$engine_repo/git/ref/tags/$tag")
revision=$(jq -r '.object.sha' <<<"$target")
if [[ "$(jq -r '.object.type' <<<"$target")" == "tag" ]]; then
  revision=$(gh api "repos/$engine_repo/git/tags/$revision" --jq '.object.sha')
fi
[[ "$revision" =~ ^[0-9a-f]{40}$ ]] || {
  echo "Could not resolve $tag to a commit revision." >&2
  exit 1
}

props="$repo_root/Directory.Build.props"
current_version=$(sed -n 's|.*<RustyEnginePackageVersion>\([^<]*\)</RustyEnginePackageVersion>.*|\1|p' "$props")

echo "newest published pair: $version"
echo "source revision:       $revision"

if [[ "$check_only" == true ]]; then
  echo "current pin:           $current_version"
  if [[ "$current_version" == "$version" ]]; then
    echo "Already pinned to the newest published pair."
  else
    echo "An update is available."
  fi
  exit 0
fi

if [[ "$current_version" == "$version" ]]; then
  echo "Already pinned to the newest published pair; Directory.Build.props is unchanged."
  exit 0
fi

sed -i -E "s|(<RustyEnginePackageVersion>)[^<]*(</RustyEnginePackageVersion>)|\1${version}\2|" "$props"
sed -i -E "s|(<RustyEnginePairSourceRevision>)[^<]*(</RustyEnginePairSourceRevision>)|\1${revision}\2|" "$props"
echo "Pinned $current_version -> $version in Directory.Build.props."

if [[ "$pin_only" == true ]]; then
  echo "Skipping install because of --pin-only."
  exit 0
fi

"$repo_root/scripts/install-engine-pair.sh"
