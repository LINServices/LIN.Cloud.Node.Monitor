# LIN.Cloud.Node.Monitor

Agente que corre en cada nodo, lee el daemon de Docker local (`Docker.DotNet`) y envía
a la API de LIN:

- **logs** de los contenedores en ejecución (`DockerLogService`)
- **métricas** de CPU y memoria por contenedor cada 10 s (`ContainerMetricsService`)

Es un host de .NET 10 (`Microsoft.Extensions.Hosting`) pensado para correr como
servicio de systemd.

## Configuración

Por variables de entorno (las escribe `install.sh` en el `EnvironmentFile` de systemd):

| Variable      | Por defecto                               | Descripción                          |
|---------------|-------------------------------------------|--------------------------------------|
| `API_URI`     | `https://api.linplatform.com/airplane/telemetry/` | base de la API de ingestión (las rutas `api/...` se añaden a ella) |
| `API_KEY`     | —                                         | se manda como cabecera `X-Api-Key`   |
| `DOCKER_HOST` | `unix:///var/run/docker.sock`             | socket/URL del daemon de Docker      |

## Compilar y ejecutar en local

```bash
dotnet run --project LIN.Cloud.Node.Monitor
```

## Publicar una release

El workflow [`.github/workflows/release.yml`](.github/workflows/release.yml) construye
un `.zip` self-contained por arquitectura (`linux-x64`, `linux-arm64`), lo sube como
artefacto de la ejecución y crea una **GitHub Release** con esos zips adjuntos.

Se dispara de dos formas:

- **empujando un tag** `vX.Y.Z` &rarr; la release toma ese nombre:

  ```bash
  git tag v1.0.0
  git push origin v1.0.0
  ```

- **a mano** desde *Actions &rarr; Release del monitor &rarr; Run workflow*, indicando
  la versión por parámetro (se crea el tag si no existe) y si es prerelease.

Cada zip, al descomprimirse, deja `install.sh` + `publish/` sin carpeta intermedia.
Ver [`deploy/linux/README.md`](deploy/linux/README.md) para instalar el servicio.

## Agente en Linux

Instalar o actualizar a la última release (descarga el zip de tu arquitectura, limpia la
instalación anterior y pide la API key por teclado):

```bash
bash <(curl -fsSL https://github.com/LINServices/LIN.Cloud.Node.Monitor/releases/latest/download/reinstall.sh)
```

Desinstalar:

```bash
bash <(curl -fsSL https://github.com/LINServices/LIN.Cloud.Node.Monitor/releases/latest/download/uninstall.sh)
```

`reinstall.sh` ya limpia la instalación anterior por su cuenta; ejecutar `uninstall.sh`
antes es opcional. Variables de `reinstall.sh`: `VERSION` (por defecto `latest`; p. ej.
`v1.0.0` para fijar una), `API_KEY` (si se define, no la pregunta), `API_URI` y
`DOCKER_HOST_VALUE`. Sin `API_URI` se usa la base por defecto
(`https://api.linplatform.com/airplane/telemetry/`).

Cada release publica `uninstall.sh` y `reinstall.sh` (de `deploy/linux/`) como assets, junto
a `lin-node-monitor-linux-x64.zip` y `lin-node-monitor-linux-arm64.zip`. Para instalar a
mano desde el zip, ver [`deploy/linux/README.md`](deploy/linux/README.md).

El host llama a `AddSystemd()`, así que la unidad usa `Type=notify` y systemd conoce
el estado real del arranque; `Restart=always` lo relevanta si el proceso muere.
