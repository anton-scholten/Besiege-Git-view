#!/usr/bin/env bash
#
# Compiles the mod into GitView/GitViewScripts.dll, using
# Besiege's OWN C# compiler rather than an installed toolchain.
#
#   ./tools/build.sh            build the mod's assembly
#   ./tools/build.sh --check    compile to a temp file only (see verify-build.sh)
#
# Why the mod is compiled ahead of time rather than shipped as a <ScriptAssembly>:
# the UI is built from UI Factory 3's prefabs, so this assembly references
# Besiege.UI.dll. A ScriptAssembly is compiled by the game at mod-load time
# against the assemblies loaded right then, and UI Factory's are not among them
# -- so that route fails with "The type or namespace name `UI' does not exist in
# the namespace `Besiege'". Every mod on the Workshop that depends on UI Factory
# ships a pre-built .dll for the same reason. A pre-built assembly only resolves
# the reference when the type is first touched, by which point UI Factory is
# loaded.
#
# This still needs no C# toolchain: it loads the game's own libmono.so and calls
# Mono.CSharp.CompilerCallableEntryPoint.InvokeCompiler in its mcs.dll, which is
# the exact code path the game used to use. gcc is needed once, to build the host.
#
# Set BESIEGE_DIR / UIFACTORY_DIR if either is not auto-detected.

set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC_DIR="$REPO_DIR/GitView/GitViewScripts"
BUILD_DIR="${TMPDIR:-/tmp}/besiege-gitview-build"
OUT="$REPO_DIR/GitView/GitViewScripts.dll"

CHECK_ONLY=0
if [[ "${1:-}" == "--check" ]]; then
    CHECK_ONLY=1
fi

# Always compile to a scratch file and move it into place afterwards. Writing
# straight to the shipped path means a failed compile can leave a truncated
# assembly behind, and -- if Besiege is open with the mod loaded -- the target
# may be mapped by the running game and refuse to be overwritten. A rename
# replaces the directory entry instead, which always works.
#
# The scratch name includes the pid: two builds running at once (say an editor
# hook and a terminal) would otherwise write the same file and one would fail
# with an unhelpful I/O error from inside the compiler.
TMP_OUT="${TMPDIR:-/tmp}/besiege-gitview-build/GitViewScripts.$$.dll"
trap 'rm -f "$TMP_OUT"' EXIT

find_besiege() {
    if [[ -n "${BESIEGE_DIR:-}" ]]; then echo "$BESIEGE_DIR"; return; fi
    local candidates=(
        "$HOME/.steam/steam/steamapps/common/Besiege"
        "$HOME/.local/share/Steam/steamapps/common/Besiege"
    )
    local vdf="$HOME/.steam/steam/steamapps/libraryfolders.vdf"
    if [[ -f "$vdf" ]]; then
        while read -r lib; do candidates+=("$lib/steamapps/common/Besiege"); done \
            < <(grep -oE '"path"[[:space:]]+"[^"]+"' "$vdf" | sed -E 's/.*"([^"]+)"$/\1/')
    fi
    for dir in "${candidates[@]}"; do
        [[ -f "$dir/Besiege_Data/Managed/mcs.dll" ]] && { echo "$dir"; return; }
    done
    return 1
}

if ! BESIEGE="$(find_besiege)"; then
    echo "Could not find Besiege. Set BESIEGE_DIR to your install directory." >&2
    exit 1
fi

DATA="$BESIEGE/Besiege_Data"
export LIBMONO="$DATA/Mono/x86_64/libmono.so"
export MANAGED="$DATA/Managed"
export MONOETC="$DATA/Mono/etc"

# UI Factory's assemblies have to be on the reference path.
find_uifactory() {
    if [[ -n "${UIFACTORY_DIR:-}" ]]; then echo "$UIFACTORY_DIR"; return; fi
    local roots=("$BESIEGE/../../workshop/content/346010/2913469777"
                 "$BESIEGE/Besiege_Data/Mods/UIFactory")
    for root in "${roots[@]}"; do
        local hit
        hit="$(find "$root" -name Besiege.UI.dll -print -quit 2>/dev/null || true)"
        [[ -n "$hit" ]] && { dirname "$hit"; return; }
    done
    return 1
}

if ! UIFACTORY="$(find_uifactory)"; then
    cat >&2 <<'EOF'
Could not find UI Factory 3 (Besiege.UI.dll).

This mod builds its interface out of UI Factory's prefabs, so it is a hard
dependency. Subscribe to Workshop item 2913469777 ("UI Factory"), or set
UIFACTORY_DIR to the folder holding Besiege.UI.dll.
EOF
    exit 1
fi
echo "Besiege:    $BESIEGE"
echo "UI Factory: $UIFACTORY"

mkdir -p "$BUILD_DIR"
HOST="$BUILD_DIR/besiegecc"
for tool in besiegecc monohost; do
    if [[ ! -x "$BUILD_DIR/$tool" || "$REPO_DIR/tools/$tool.c" -nt "$BUILD_DIR/$tool" ]]; then
        echo "Building $tool host..."
        gcc -O1 -o "$BUILD_DIR/$tool" "$REPO_DIR/tools/$tool.c" -ldl
    fi
done

if pgrep -x Besiege >/dev/null 2>&1 || pgrep -f 'Besiege\.x86' >/dev/null 2>&1; then
    echo "Note: Besiege appears to be running. The build itself is fine, but the"
    echo "      game will not pick up the new assembly until you restart it."
fi

echo "Compiling $(ls "$SRC_DIR"/*.cs | wc -l) source files with Besiege's compiler,"
echo "  $HOST (built $(date -r "$HOST" '+%H:%M:%S' 2>/dev/null || echo '?'))"
set +e
"$HOST" -target:library -out:"$TMP_OUT" \
    -lib:"$MANAGED" -lib:"$UIFACTORY" \
    -r:UnityEngine.dll -r:UnityEngine.UI.dll -r:Assembly-CSharp.dll \
    -r:Assembly-CSharp-firstpass.dll -r:System.dll -r:System.Core.dll \
    -r:Besiege.UI.dll -r:Besiege.UI.Bridge.dll \
    "$SRC_DIR"/*.cs
rc=$?
set -e

if [[ $rc -ne 0 ]]; then
    cat >&2 <<'EOF'

Build FAILED. The previously built assembly, if any, was left untouched.

If the output above is a list of CS#### errors, that is an ordinary compile
error -- fix the source. Otherwise:

  "the compiler threw a managed exception"
      The exception text is printed above it. Compiling holds the game's whole
      assembly set in memory, so if it is an OutOfMemoryException, close Besiege
      (or anything else large) and try again.

  a SIGSEGV inside Mono.CSharp
      A construct this ancient compiler cannot handle. The known case is any
      `enum` declaration; use int constants instead. See docs/MODDING-NOTES.md.
EOF
    echo >&2 "Environment, in case it is relevant:"
    echo >&2 "  TMPDIR=${TMPDIR:-unset}  MONO_PATH=${MONO_PATH:-unset}  LANG=${LANG:-unset}"
    echo >&2 "  free memory: $(free -h 2>/dev/null | awk '/^Mem:/{print $7" available of "$2}' || echo unknown)"
    exit $rc
fi

# Compiling cleanly says nothing about whether the mod loader will accept the
# result: it also scans for blacklisted namespaces and refuses the whole assembly
# over a single reference. `e.GetType().Name` is enough to fail, because Type.Name
# is declared on System.Reflection.MemberInfo. Catch that here, not at launch.
BLACKLIST="$BUILD_DIR/blacklist.exe"
if [[ ! -f "$BLACKLIST" || "$REPO_DIR/tools/tests/BlacklistCheck.cs" -nt "$BLACKLIST" ]]; then
    "$HOST" -target:exe -out:"$BLACKLIST" -lib:"$MANAGED" -r:System.dll \
        "$REPO_DIR/tools/tests/BlacklistCheck.cs" >/dev/null 2>&1
fi
if [[ -f "$BLACKLIST" ]]; then
    set +e
    TARGET_ASM="$BLACKLIST" "$BUILD_DIR/monohost" "$TMP_OUT" \
        "$UIFACTORY/Besiege.UI.dll" "$UIFACTORY/Besiege.UI.Bridge.dll"
    scan_rc=$?
    set -e
    if [[ $scan_rc -ne 0 ]]; then
        echo >&2
        echo >&2 "Refusing to install an assembly Besiege would reject."
        exit 1
    fi
else
    echo "(blacklist checker unavailable; skipping that check)" >&2
fi

if [[ $CHECK_ONLY -eq 1 ]]; then
    echo "Build OK (check only; $(stat -c%s "$TMP_OUT") bytes, not installed)"
else
    mv -f "$TMP_OUT" "$OUT"
    echo "Build OK -> $OUT"
    echo "Mod.xml loads this assembly directly; no in-game compile step is involved."
fi
