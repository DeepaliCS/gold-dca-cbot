#!/bin/bash
# Usage: ./scripts/deploy.sh [bot-name]
# Defaults to GoldDcaBot if no argument given

BOT_NAME="${1:-GoldDcaBot}"

SRC="$HOME/projects/gold-dca-cbot/src/$BOT_NAME/$BOT_NAME.cs"
DEST_DIR="/mnt/c/Users/deepa/OneDrive/Documents/cAlgo/Sources/Robots/$BOT_NAME/$BOT_NAME"
DEST="$DEST_DIR/$BOT_NAME.cs"

if [ ! -f "$SRC" ]; then
    echo "✗ Source not found: $SRC"
    exit 1
fi

mkdir -p "$DEST_DIR"
cp "$SRC" "$DEST" && echo "✓ Deployed $BOT_NAME at $(date +%H:%M:%S)"
