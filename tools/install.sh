#!/usr/bin/env bash
#
# Installs the mod into Besiege's Mods folder.
#
#   ./tools/install.sh            symlink the mod (best for development -- a
#                                 rebuilt assembly is picked up on restart)
#   ./tools/install.sh --copy     copy the mod instead (for handing it to someone)
#   ./tools/install.sh --uninstall
#
# Set BESIEGE_DIR to point at your install if it is not auto-detected, e.g.
#   BESIEGE_DIR="$HOME/.steam/steam/steamapps/common/Besiege" ./tools/install.sh

set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MOD_NAME="GitView"
SRC="$REPO_DIR/$MOD_NAME"

find_besiege() {
    if [[ -n "${BESIEGE_DIR:-}" ]]; then
        echo "$BESIEGE_DIR"
        return
    fi

    local candidates=(
        "$HOME/.steam/steam/steamapps/common/Besiege"
        "$HOME/.local/share/Steam/steamapps/common/Besiege"
        "$HOME/Library/Application Support/Steam/steamapps/common/Besiege"
    )
    # Any additional Steam library folders configured on this machine.
    local vdf="$HOME/.steam/steam/steamapps/libraryfolders.vdf"
    if [[ -f "$vdf" ]]; then
        while read -r lib; do
            candidates+=("$lib/steamapps/common/Besiege")
        done < <(grep -oE '"path"[[:space:]]+"[^"]+"' "$vdf" | sed -E 's/.*"([^"]+)"$/\1/')
    fi

    for dir in "${candidates[@]}"; do
        if [[ -d "$dir/Besiege_Data/Mods" ]]; then
            echo "$dir"
            return
        fi
    done
    return 1
}

if ! BESIEGE="$(find_besiege)"; then
    echo "Could not find Besiege. Set BESIEGE_DIR to your install directory." >&2
    exit 1
fi

MODS="$BESIEGE/Besiege_Data/Mods"
DEST="$MODS/$MOD_NAME"
echo "Besiege:  $BESIEGE"
echo "Mods dir: $MODS"

# The mod ships a pre-built assembly (see Mod.xml), so building is part of
# installing, and has to happen before --copy takes a snapshot of the folder.
# build.sh uses Besiege's own compiler -- no C# toolchain needed -- and refuses
# to continue if UI Factory is missing, which is the dependency it resolves.
if [[ "${1:-}" != "--uninstall" ]]; then
    if ! BESIEGE_DIR="$BESIEGE" "$REPO_DIR/tools/build.sh"; then
        cat >&2 <<'EOF'

The mod's assembly could not be built, so nothing was installed.
Fix the error above and re-run this script.
EOF
        exit 1
    fi

    # Check the manifest before installing anything. A malformed Mod.xml does not
    # produce an error in game: the mod simply never appears in the list, which is
    # indistinguishable from not having installed it. (The easy way to get there
    # is a "--" inside an XML comment, which XML does not allow.)
    if command -v python3 >/dev/null 2>&1; then
        if ! python3 - "$SRC" <<'PY'
import os, sys, xml.dom.minidom
src = sys.argv[1]
try:
    root = xml.dom.minidom.parse(os.path.join(src, "Mod.xml")).documentElement
except Exception as e:
    sys.exit("Mod.xml is not valid XML: %s" % e)
missing = [a.getAttribute("path") for a in root.getElementsByTagName("Assembly")
           if not os.path.exists(os.path.join(src, a.getAttribute("path")))]
if missing:
    sys.exit("Mod.xml lists assemblies that are not there: %s" % ", ".join(missing))
PY
        then
            echo "Refusing to install: the manifest is broken (see above)." >&2
            exit 1
        fi
    fi
    echo
fi

case "${1:-}" in
    --uninstall)
        if [[ -L "$DEST" ]]; then
            rm "$DEST"
            echo "Removed symlink $DEST"
        elif [[ -d "$DEST" ]]; then
            rm -rf "$DEST"
            echo "Removed $DEST"
        else
            echo "Nothing installed at $DEST"
        fi
        find "$MODS/.CompiledAssemblies" -name '*_GitViewScripts.dll' -delete 2>/dev/null || true
        exit 0
        ;;
    --copy)
        rm -rf "$DEST"
        cp -r "$SRC" "$DEST"
        echo "Copied mod to $DEST"
        ;;
    "")
        # Replace whatever is there, then link.
        [[ -L "$DEST" ]] && rm "$DEST"
        [[ -d "$DEST" ]] && rm -rf "$DEST"
        ln -s "$SRC" "$DEST"
        echo "Linked $DEST -> $SRC"
        ;;
    *)
        echo "Unknown option: $1" >&2
        exit 1
        ;;
esac

# Left over from when this mod was a ScriptAssembly: Besiege caches those builds
# and reuses them forever. Nothing reads it now, but leaving a stale copy around
# is just something to trip over later.
find "$MODS/.CompiledAssemblies" -name '*_GitViewScripts.dll' -delete 2>/dev/null || true

cat <<'EOF'

Done. Next:
  1. Subscribe to UI Factory 3 (Workshop item 2913469777) and enable it. The
     history window is built out of its prefabs and will not appear without it.
  2. Start Besiege and enable "Git View" in the mods menu.
  3. Enter a level or the sandbox. The mod loads then, not at startup.
  4. Open the load-machine screen. Machines with autosaves behind them get an
     extra button; press it to open the newest version and its history.
  5. Ctrl+Y hides and shows the history window and its overlay.

Note: the mod ID is generated into Mod.xml the first time the game loads the mod.
If you installed with a symlink, that write lands in your working copy -- commit it,
it is meant to stay stable for the life of the mod.
EOF
