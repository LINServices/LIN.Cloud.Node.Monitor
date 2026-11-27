#!/usr/bin/env bash
# Limpia la instalación actual de LIN Cloud Node Monitor e instala la última release.
# Pide la API key por teclado (no queda en el historial del shell).
#
#   ./reinstall.sh
#   bash <(curl -fsSL https://github.com/LINServices/LIN.Cloud.Node.Monitor/releases/latest/download/reinstall.sh)
#   VERSION=v1.0.0 API_URI=https://api.linplatform.com/airplane/telemetry/ ./reinstall.sh
#
# Variables:
#   VERSION   release a instalar (por defecto "latest"; p. ej. v1.0.0 para fijar una)
#   API_URI   base de la API; si no se define, el agente usa su valor por defecto
#   API_KEY   si ya viene definida, no se pregunta
#   DOCKER_HOST_VALUE  socket/URL de Docker si no es el estándar
set -euo pipefail

VERSION="${VERSION:-latest}"
API_URI="${API_URI:-}"
DOCKER_HOST_VALUE="${DOCKER_HOST_VALUE:-}"

case "$(uname -m)" in
  x86_64|amd64)  RID="linux-x64" ;;
  aarch64|arm64) RID="linux-arm64" ;;
  *) echo "Arquitectura no soportada: $(uname -m)" >&2; exit 1 ;;
esac

ZIP_NAME="lin-node-monitor-$RID.zip"
REPO_URL="https://github.com/LINServices/LIN.Cloud.Node.Monitor/releases"
if [[ "$VERSION" == "latest" ]]; then
  BASE_URL="$REPO_URL/latest/download"
else
  BASE_URL="$REPO_URL/download/$VERSION"
fi
URL="$BASE_URL/$ZIP_NAME"
WORK_DIR="$HOME/lin-node-monitor"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

KEY="${API_KEY:-}"
if [[ -z "$KEY" ]]; then
  read -r -s -p "API key (Enter para omitirla): " KEY
  echo
fi

echo "==> Dependencias"
if ! command -v unzip >/dev/null || ! command -v wget >/dev/null; then
  sudo apt update
  sudo apt install -y unzip wget
fi

echo "==> Limpiando instalación actual"
UNINSTALL="$SCRIPT_DIR/uninstall.sh"
if [[ ! -f "$UNINSTALL" ]]; then
  # Ejecutado sin el repositorio (p. ej. con curl): se baja de la release.
  UNINSTALL="$(mktemp)"
  wget -q -O "$UNINSTALL" "$BASE_URL/uninstall.sh"
fi
bash "$UNINSTALL"

echo "==> Descargando release: $VERSION ($RID)"
rm -rf "$WORK_DIR" && mkdir -p "$WORK_DIR"
cd "$WORK_DIR"
wget -q --show-progress "$URL"
unzip -q "$ZIP_NAME"
chmod +x install.sh

echo "==> Instalando"
ARGS=()
[[ -n "$API_URI" ]]           && ARGS+=(--api-uri "$API_URI")
[[ -n "$KEY" ]]               && ARGS+=(--api-key "$KEY")
[[ -n "$DOCKER_HOST_VALUE" ]] && ARGS+=(--docker-host "$DOCKER_HOST_VALUE")
sudo ./install.sh ${ARGS[@]+"${ARGS[@]}"}

echo "==> Listo. Estado: systemctl status lin-node-monitor"
