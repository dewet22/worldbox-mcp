#!/usr/bin/env bash
# Linux/macOS installer for the WorldBoxBridge mod.
#
# WorldBox on Linux uses Unity's native Mono build and is often run via Steam Proton on Linux.
# This script targets a native (Mac/Linux) WorldBox install. Proton installs are usually
# handled like Windows installs — use install-mod.ps1 inside the Proton prefix instead.

set -euo pipefail

BEPINEX_VERSION="${BEPINEX_VERSION:-5.4.23.2}"
WORLDBOX_PATH="${WORLDBOX_PATH:-}"

err() { printf '\033[31m✗ %s\033[0m\n' "$*" >&2; exit 1; }
ok()  { printf '\033[32m✓ %s\033[0m\n' "$*"; }
log() { printf '\033[36m→ %s\033[0m\n' "$*"; }

find_worldbox() {
    local candidates=(
        "$HOME/.steam/steam/steamapps/common/worldbox"
        "$HOME/.local/share/Steam/steamapps/common/worldbox"
        "$HOME/Library/Application Support/Steam/steamapps/common/worldbox"
    )
    for c in "${candidates[@]}"; do
        if [[ -d "$c" ]]; then
            echo "$c"
            return
        fi
    done
}

detect_platform_zip() {
    local uname_s
    uname_s="$(uname -s)"
    case "$uname_s" in
        Linux)  echo "BepInEx_unix_${BEPINEX_VERSION}.zip" ;;
        Darwin) echo "BepInEx_macos_${BEPINEX_VERSION}.zip" ;;
        *)      err "Unsupported OS: $uname_s. Use install-mod.ps1 on Windows." ;;
    esac
}

if [[ -z "$WORLDBOX_PATH" ]]; then
    WORLDBOX_PATH="$(find_worldbox)"
fi
[[ -z "$WORLDBOX_PATH" ]] && err "WorldBox install not found. Export WORLDBOX_PATH=<path> and retry."
[[ ! -f "$WORLDBOX_PATH/worldbox" && ! -f "$WORLDBOX_PATH/worldbox.x86_64" && ! -d "$WORLDBOX_PATH/worldbox.app" ]] \
    && err "No WorldBox binary found in $WORLDBOX_PATH"
ok "WorldBox at $WORLDBOX_PATH"

if [[ -f "$WORLDBOX_PATH/BepInEx/core/BepInEx.dll" ]]; then
    ok "BepInEx already installed"
else
    zip_name="$(detect_platform_zip)"
    log "Installing BepInEx $BEPINEX_VERSION ($zip_name)"
    tmp="$(mktemp -d)"
    curl -fsSL -o "$tmp/bep.zip" \
        "https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VERSION}/${zip_name}"
    unzip -q "$tmp/bep.zip" -d "$WORLDBOX_PATH"
    rm -rf "$tmp"
    ok "BepInEx installed"
fi

log "Fetching latest WorldBoxBridge release..."
asset_url=$(curl -fsSL https://api.github.com/repos/fullya99/worldbox-mcp/releases/latest \
    | grep -E '"browser_download_url".*WorldBoxBridge-v.*\.zip' \
    | head -n1 | cut -d'"' -f4)
[[ -z "$asset_url" ]] && err "Could not find WorldBoxBridge release asset"

tmp="$(mktemp -d)"
curl -fsSL -o "$tmp/wbb.zip" "$asset_url"
unzip -q "$tmp/wbb.zip" -d "$tmp"
mkdir -p "$WORLDBOX_PATH/BepInEx/plugins"
cp -f "$tmp/WorldBoxBridge/WorldBoxBridge.dll" "$WORLDBOX_PATH/BepInEx/plugins/"
rm -rf "$tmp"
ok "WorldBoxBridge.dll installed"

cfg_dir="$WORLDBOX_PATH/BepInEx/config"
cfg="$cfg_dir/WorldBoxBridge.cfg"
mkdir -p "$cfg_dir"
if [[ ! -f "$cfg" ]]; then
    token="$(LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c48)"
    cat >"$cfg" <<EOF
## WorldBoxBridge configuration
## Generated $(date -Iseconds)

[Bridge]
enabled = true
host    = 127.0.0.1
port    = 8723
token   = $token
EOF
    chmod 600 "$cfg"
    ok "Generated $cfg with a fresh token"
else
    ok "Existing $cfg preserved"
fi

cat <<EOF

Install complete.
Next:
  1. Enable Experimental Mode in WorldBox (Settings → Experimental Mode).
  2. Launch WorldBox. Check $WORLDBOX_PATH/BepInEx/LogOutput.log for:
       [Info: WorldBoxBridge] listening on 127.0.0.1:8723
EOF
