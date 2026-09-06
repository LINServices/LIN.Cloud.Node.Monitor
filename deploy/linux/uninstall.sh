#!/usr/bin/env bash
# Desinstala LIN Cloud Node Monitor.
#
#   sudo ./uninstall.sh [--keep-config]
set -euo pipefail

UNIT_NAME=lin-node-monitor.service
INSTALL_DIR=/opt/lin-node-monitor
CONFIG_DIR=/etc/lin-node-monitor
KEEP_CONFIG=0

[[ "${1:-}" == "--keep-config" ]] && KEEP_CONFIG=1

if [[ $EUID -ne 0 ]]; then
  echo "Este script necesita privilegios de root." >&2
  exit 1
fi

echo "==> Deteniendo y deshabilitando el servicio"
systemctl disable --now "$UNIT_NAME" 2>/dev/null || true
rm -f "/etc/systemd/system/$UNIT_NAME"
systemctl daemon-reload

echo "==> Borrando $INSTALL_DIR"
rm -rf "$INSTALL_DIR"

if [[ "$KEEP_CONFIG" -eq 0 ]]; then
  echo "==> Borrando $CONFIG_DIR"
  rm -rf "$CONFIG_DIR"
else
  echo "==> Se conserva $CONFIG_DIR"
fi

echo "==> Desinstalado."
