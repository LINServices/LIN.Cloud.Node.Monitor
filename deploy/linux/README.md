# Despliegue en Linux

Requisitos: Linux con **systemd** y **Docker** en el nodo (x64 o arm64). El binario es
self-contained: no hace falta instalar .NET.

## Instalación rápida (recomendada)

Descarga la última release, limpia cualquier instalación previa e instala el servicio.
Pide la API key por teclado, así no queda en el historial del shell:

```bash
bash <(curl -fsSL https://github.com/LINServices/LIN.Cloud.Node.Monitor/releases/latest/download/reinstall.sh)
```

Variables opcionales:

| Variable            | Por defecto | Para qué                                              |
|---------------------|-------------|-------------------------------------------------------|
| `VERSION`           | `latest`    | release a instalar (p. ej. `v1.0.0`)                  |
| `API_KEY`           | se pregunta | si se define, no se pide por teclado                  |
| `API_URI`           | del agente  | base de la API, solo si difiere de la de por defecto  |
| `DOCKER_HOST_VALUE` | del agente  | socket/URL de Docker si no es el estándar             |

```bash
VERSION=v1.0.0 API_KEY=TU_CLAVE bash <(curl -fsSL https://github.com/LINServices/LIN.Cloud.Node.Monitor/releases/latest/download/reinstall.sh)
```

## Desinstalación rápida

```bash
bash <(curl -fsSL https://github.com/LINServices/LIN.Cloud.Node.Monitor/releases/latest/download/uninstall.sh)
```

Para conservar la configuración (`/etc/lin-node-monitor`) añade `--keep-config`:

```bash
bash <(curl -fsSL https://github.com/LINServices/LIN.Cloud.Node.Monitor/releases/latest/download/uninstall.sh) --keep-config
```

## Instalación manual desde el zip

El zip `lin-node-monitor-linux-x64.zip` (o `-arm64`) de cada [release](../../releases)
contiene:

```
install.sh                  # instala el servicio
uninstall.sh                # lo quita
reinstall.sh                # desinstala + descarga la última release + instala
lin-node-monitor.service    # unidad de systemd
publish/                    # binario self-contained
  LIN.Cloud.Node.Monitor
  ...
```

```bash
unzip lin-node-monitor-linux-x64.zip -d lin-node-monitor
cd lin-node-monitor
sudo ./install.sh --api-key TU_CLAVE
```

Todos los flags son opcionales:

| Flag             | Por defecto                                       | Para qué                                     |
|------------------|---------------------------------------------------|----------------------------------------------|
| `--api-uri`      | `https://api.linplatform.com/airplane/telemetry/` | base de la API (solo si difiere del default) |
| `--api-key`      | —                                                 | cabecera `X-Api-Key`                         |
| `--docker-host`  | `unix:///var/run/docker.sock`                     | socket/URL de Docker si no es el estándar    |
| `--source`       | carpeta `publish/` junto al script                | ruta al binario publicado                    |

`install.sh`:

1. copia `publish/` a `/opt/lin-node-monitor`
2. escribe `/etc/lin-node-monitor/lin-node-monitor.env` (modo `0600`) solo con las
   claves que se pasaron; las demás usan el valor por defecto del agente
   (si ya existía, deja copia en `lin-node-monitor.env.bak`)
3. instala la unidad en `/etc/systemd/system/`, hace `daemon-reload`, `enable` y `restart`

Desinstalar desde el zip:

```bash
sudo ./uninstall.sh              # borra servicio, binario y configuración
sudo ./uninstall.sh --keep-config
```

## Operar

```bash
systemctl status lin-node-monitor
systemctl restart lin-node-monitor
journalctl -u lin-node-monitor -f
```

## Cambiar configuración

Edita `/etc/lin-node-monitor/lin-node-monitor.env` y ejecuta
`sudo systemctl restart lin-node-monitor`.

| Variable      | Por defecto                                       |
|---------------|---------------------------------------------------|
| `API_URI`     | `https://api.linplatform.com/airplane/telemetry/` |
| `API_KEY`     | —                                                 |
| `DOCKER_HOST` | `unix:///var/run/docker.sock`                     |

## Rutas que usa el agente

Se añaden a `API_URI` (el prefijo `/airplane/telemetry/` se conserva):

- `POST   .../api/logs`
- `POST   .../api/metrics`
- `GET|POST .../api/checkpoints/{containerId}`

## Publicar una release

Empuja un tag `vX.Y.Z` (o lanza *Actions → Release del monitor* a mano).
[`release.yml`](../../.github/workflows/release.yml) adjunta los zips y los scripts
`uninstall.sh` / `reinstall.sh` a la release.
