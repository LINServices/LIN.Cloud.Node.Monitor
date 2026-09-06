#!/usr/bin/env bash
# Instala LIN Cloud Node Monitor como unidad de systemd.
#
#   sudo ./install.sh --api-uri http://datalake.linplatform.com:5007 \
#                     --api-key <clave> \
#                     [--docker-host unix:///var/run/docker.sock] \
#                     [--source ./publish]
#
# El zip de la release trae este script junto a la carpeta publish/, así que
# desde una máquina recién descomprimida basta con:
#
#   sudo ./install.sh --api-uri <uri> --api-key <clave>
set -euo pipefail

INSTALL_DIR=/opt/lin-node-monitor
CONFIG_DIR=/etc/lin-node-monitor
ENV_FILE="$CONFIG_DIR/lin-node-monitor.env"
UNIT_NAME=lin-node-monitor.service
BINARY=LIN.Cloud.Node.Monitor
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_DIR="$SCRIPT_DIR/publish"

API_URI=""
API_KEY=""
DOCKER_HOST_VALUE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --api-uri)     API_URI="${2:-}";           shift 2 ;;
    --api-key)     API_KEY="${2:-}";           shift 2 ;;
    --docker-host) DOCKER_HOST_VALUE="${2:-}"; shift 2 ;;
    --source)      SOURCE_DIR="${2:-}";        shift 2 ;;
    -h|--help)     grep '^#' "$0" | cut -c3-;  exit 0 ;;
    *) echo "Opción desconocida: $1" >&2; exit 1 ;;
  esac
done

if [[ $EUID -ne 0 ]]; then
  echo "Este script necesita privilegios de root." >&2
  exit 1
fi

if [[ -z "$API_URI" ]]; then
  echo "Falta --api-uri (por ejemplo http://datalake.linplatform.com:5007)" >&2
  exit 1
fi

if [[ ! -x "$SOURCE_DIR/$BINARY" ]]; then
  echo "No se encontró el binario publicado en $SOURCE_DIR/$BINARY" >&2
  echo "Publícalo antes con:" >&2
  echo "  dotnet publish LIN.Cloud.Node.Monitor/LIN.Cloud.Node.Monitor.csproj -c Release -r linux-x64 --self-contained true -o $SOURCE_DIR" >&2
  exit 1
fi

echo "==> Deteniendo el servicio si ya estaba instalado"
systemctl stop "$UNIT_NAME" 2>/dev/null || true

echo "==> Copiando binarios a $INSTALL_DIR"
install -d -m 0755 "$INSTALL_DIR"
rm -rf "${INSTALL_DIR:?}"/*
cp -a "$SOURCE_DIR"/. "$INSTALL_DIR"/
chmod 0755 "$INSTALL_DIR/$BINARY"

echo "==> Escribiendo configuración en $ENV_FILE"
install -d -m 0755 "$CONFIG_DIR"
if [[ -f "$ENV_FILE" ]]; then
  cp -a "$ENV_FILE" "$ENV_FILE.bak"
  echo "    (copia previa guardada en $ENV_FILE.bak)"
fi
umask 077
{
  echo "# Generado por install.sh el $(date --iso-8601=seconds)"
  echo "API_URI=$API_URI"
  echo "API_KEY=$API_KEY"
  [[ -n "$DOCKER_HOST_VALUE" ]] && echo "DOCKER_HOST=$DOCKER_HOST_VALUE"
} > "$ENV_FILE"
chmod 0600 "$ENV_FILE"

echo "==> Instalando la unidad de systemd"
install -m 0644 "$SCRIPT_DIR/$UNIT_NAME" "/etc/systemd/system/$UNIT_NAME"
systemctl daemon-reload
systemctl enable "$UNIT_NAME"
systemctl restart "$UNIT_NAME"

echo "==> Listo. Estado del servicio:"
systemctl --no-pager status "$UNIT_NAME" || true

echo
echo "Logs en vivo:  journalctl -u $UNIT_NAME -f"
