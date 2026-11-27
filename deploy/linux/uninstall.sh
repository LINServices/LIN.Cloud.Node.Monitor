#!/usr/bin/env bash
# Desinstala LIN Cloud Node Monitor (servicio, binarios y configuración).
#
#   ./uninstall.sh
#   ./uninstall.sh --keep-config
#   bash <(curl -fsSL https://github.com/LINServices/LIN.Cloud.Node.Monitor/releases/latest/download/uninstall.sh)
set -euo pipefail

UNIT_NAME="lin-node-monitor.service"
INSTALL_DIR="/opt/lin-node-monitor"
CONFIG_DIR="/etc/lin-node-monitor"

KEEP_CONFIG=0

for arg in "$@"; do
  case "$arg" in
    --keep-config) KEEP_CONFIG=1 ;;
    -h|--help)     grep '^#' "$0" | cut -c3-; exit 0 ;;
    *) echo "Argumento desconocido: $arg" >&2; echo "Uso: $0 [--keep-config]" >&2; exit 1 ;;
  esac
done

echo "==> LIN Cloud Node Monitor - Desinstalación"

# Como root no hace falta sudo; si no, se piden privilegios una sola vez.
SUDO=""
if [[ $EUID -ne 0 ]]; then
  SUDO="sudo"
  if ! sudo -v; then
    echo "No se pudieron obtener privilegios de sudo." >&2
    exit 1
  fi
fi

echo "==> Deteniendo y eliminando el servicio"
$SUDO systemctl disable --now "$UNIT_NAME" 2>/dev/null || true
$SUDO rm -f "/etc/systemd/system/$UNIT_NAME"
$SUDO systemctl daemon-reload
$SUDO systemctl reset-failed "$UNIT_NAME" 2>/dev/null || true

echo "==> Eliminando instalación: $INSTALL_DIR"
$SUDO rm -rf "$INSTALL_DIR"

if [[ "$KEEP_CONFIG" -eq 0 ]]; then
  echo "==> Eliminando configuración: $CONFIG_DIR"
  $SUDO rm -rf "$CONFIG_DIR"
else
  echo "==> Conservando configuración: $CONFIG_DIR"
fi

echo "==> LIN Cloud Node Monitor desinstalado."
