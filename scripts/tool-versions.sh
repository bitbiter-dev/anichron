#!/usr/bin/env bash
# Usage: tool-versions.sh [tool-manifest] [packages-props]
#
# Exits 0 when the dotnet-ef tool in the manifest (default .config/dotnet-tools.json) has the
# same version as Microsoft.EntityFrameworkCore in the props file (default
# src/Directory.Packages.props). Otherwise exits 1 and names both versions.
#
# A mismatched dotnet-ef prints its version notice to stdout, inside docs/schema.sql
# (scripts/derive-db-docs.sh). Dependabot bumps the two in one PR only while the manifest is
# at .config/dotnet-tools.json, the one place it looks; this catches it when they still part.
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
manifest="${1:-$root/.config/dotnet-tools.json}"
props="${2:-$root/src/Directory.Packages.props}"

for file in "$manifest" "$props"; do
  if [ ! -f "$file" ]; then
    echo "$file does not exist" >&2
    exit 2
  fi
done

tool=$(jq -r '.tools["dotnet-ef"].version // empty' "$manifest")
package=$(sed -n 's/.*<PackageVersion Include="Microsoft\.EntityFrameworkCore" Version="\([^"]*\)".*/\1/p' "$props")

if [ -z "$tool" ]; then
  echo "dotnet-ef is not in $manifest" >&2
  exit 2
fi
if [ -z "$package" ]; then
  echo "Microsoft.EntityFrameworkCore is not in $props" >&2
  exit 2
fi

if [ "$tool" != "$package" ]; then
  echo "dotnet-ef $tool does not match Microsoft.EntityFrameworkCore $package." >&2
  echo "Set dotnet-ef in .config/dotnet-tools.json to $package." >&2
  exit 1
fi
