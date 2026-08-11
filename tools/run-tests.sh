#!/usr/bin/env bash
#
# Runs the headless tests: the block diff, the version-name parsing and the
# sorting -- everything this mod can be wrong about without a running game.
#
#   ./tools/run-tests.sh
#
# Uses Besiege's own compiler and Mono runtime, like tools/build.sh, so there is
# no toolchain to install. Nothing here touches the game, the disk or the network.

set -euo pipefail
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="$REPO_DIR/GitView/GitViewScripts"
BUILD_DIR="${TMPDIR:-/tmp}/besiege-gitview-build"

find_besiege() {
    if [[ -n "${BESIEGE_DIR:-}" ]]; then echo "$BESIEGE_DIR"; return; fi
    local candidates=(
        "$HOME/.steam/steam/steamapps/common/Besiege"
        "$HOME/.local/share/Steam/steamapps/common/Besiege"
    )
    local vdf
    for vdf in "$HOME/.steam/steam/steamapps/libraryfolders.vdf" \
               "$HOME/.local/share/Steam/steamapps/libraryfolders.vdf"; do
        [[ -f "$vdf" ]] || continue
        while read -r lib; do candidates+=("$lib/steamapps/common/Besiege"); done \
            < <(grep -oE '"path"[[:space:]]+"[^"]+"' "$vdf" | sed -E 's/.*"([^"]+)"$/\1/')
    done
    local dir
    for dir in "${candidates[@]}"; do
        [[ -f "$dir/Besiege_Data/Managed/mcs.dll" ]] && { echo "$dir"; return; }
    done
    return 1
}

BESIEGE="$(find_besiege)" || { echo "Set BESIEGE_DIR to your Besiege install." >&2; exit 1; }
DATA="$BESIEGE/Besiege_Data"
export LIBMONO="$DATA/Mono/x86_64/libmono.so" MANAGED="$DATA/Managed" MONOETC="$DATA/Mono/etc"

mkdir -p "$BUILD_DIR"
for tool in besiegecc monohost; do
    if [[ ! -x "$BUILD_DIR/$tool" || "$REPO_DIR/tools/$tool.c" -nt "$BUILD_DIR/$tool" ]]; then
        gcc -O1 -o "$BUILD_DIR/$tool" "$REPO_DIR/tools/$tool.c" -ldl
    fi
done

# Only the engine-free sources are compiled in. They reference nothing from the
# game or from UI Factory, which is the property this file is really testing: if
# the diff ever needs a running Besiege to be checked, this build breaks first.
"$BUILD_DIR/besiegecc" -target:exe -out:"$BUILD_DIR/tests.exe" -lib:"$MANAGED" \
    -r:UnityEngine.dll -r:System.dll -r:System.Core.dll \
    "$SRC_DIR/BlockRecord.cs" "$SRC_DIR/MachineSnapshot.cs" "$SRC_DIR/BlockDiff.cs" \
    "$SRC_DIR/VersionEntry.cs" "$SRC_DIR/RowSort.cs" \
    "$REPO_DIR/tools/tests/DiffTests.cs"

TARGET_ASM="$BUILD_DIR/tests.exe" "$BUILD_DIR/monohost"
