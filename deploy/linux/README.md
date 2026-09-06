# Despliegue en Linux

El zip `lin-node-monitor-linux-x64.zip` de cada [release](../../releases) contiene:

```
install.sh                  # instala el servicio
uninstall.sh                # lo quita
lin-node-monitor.service    # unidad de systemd
publish/                    # binario self-contained (no necesita .NET instalado)
  LIN.Cloud.Node.Monitor
  ...
```

## Instalar

```bash
unzip lin-node-monitor-linux-x64.zip -d lin-node-monitor
cd lin-node-monitor
sudo ./install.sh \
  --api-uri http://datalake.linplatform.com:5007 \
  --api-key TU_CLAVE
```

Opcionales:

| Flag             | Para qué                                                        |
|------------------|----------------------------------------------------------------|
| `--docker-host`  | socket/URL de Docker si no es `unix:///var/run/docker.sock`    |
| `--source`       | ruta a la carpeta `publish/` (por defecto, junto al script)    |

`install.sh`:

1. copia `publish/` a `/opt/lin-node-monitor`
2. escribe `/etc/lin-node-monitor/lin-node-monitor.env` (modo `0600`) con `API_URI` / `API_KEY`
3. instala la unidad en `/etc/systemd/system/`, hace `daemon-reload`, `enable` y `restart`

## Operar

```bash
systemctl status lin-node-monitor
systemctl restart lin-node-monitor
journalctl -u lin-node-monitor -f
```

## Cambiar configuración

Edita `/etc/lin-node-monitor/lin-node-monitor.env` y `sudo systemctl restart lin-node-monitor`.

## Desinstalar

```bash
sudo ./uninstall.sh              # borra binario + configuración
sudo ./uninstall.sh --keep-config
```
