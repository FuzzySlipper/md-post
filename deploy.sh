#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="${HOME}/.local/bin"
PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "Building mdpost..."
dotnet publish "$PROJECT_DIR/src/MdPost/MdPost.csproj" \
    -c Release \
    -o "$PROJECT_DIR/publish" \
    --nologo -v quiet

mkdir -p "$INSTALL_DIR"

# Symlink the published binary
ln -sf "$PROJECT_DIR/publish/mdpost" "$INSTALL_DIR/mdpost"

echo "Installed: $INSTALL_DIR/mdpost -> $PROJECT_DIR/publish/mdpost"
echo "Run 'mdpost help' to verify."
