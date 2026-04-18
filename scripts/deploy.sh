#!/bin/bash
# Deploys GoldDcaBot.cs from WSL repo → cTrader's Robots folder
# Usage: ./scripts/deploy.sh

SRC="$HOME/projects/gold-dca-cbot/src/GoldDcaBot/GoldDcaBot.cs"
DEST_DIR="/mnt/c/Users/deepa/OneDrive/Documents/cAlgo/Sources/Robots/GoldDcaBot/GoldDcaBot"
DEST="$DEST_DIR/GoldDcaBot.cs"

# Check source exists
if [ ! -f "$SRC" ]; then
    echo "✗ Source file not found: $SRC"
    exit 1
fi

# Ensure destination folder exists
mkdir -p "$DEST_DIR"

# Copy
cp "$SRC" "$DEST" && echo "✓ Deployed at $(date +%H:%M:%S) → cTrader"
