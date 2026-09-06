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
| `API_URI`     | `http://datalake.linplatform.com:5007`    | base de la API de ingestión          |
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

## Instalar como servicio (resumen)

```bash
unzip lin-node-monitor-linux-x64.zip -d lin-node-monitor && cd lin-node-monitor
sudo ./install.sh --api-uri http://datalake.linplatform.com:5007 --api-key TU_CLAVE
```

El host llama a `AddSystemd()`, así que la unidad usa `Type=notify` y systemd conoce
el estado real del arranque; `Restart=always` lo relevanta si el proceso muere.
