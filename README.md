# GalleryDlManager

A two-container toolchain that lets an AI agent download media galleries via MCP:

- **GalleryDl.WebApi** — ASP.NET Core API in a container with Python and
  [gallery-dl](https://github.com/mikf/gallery-dl). On request it runs gallery-dl (via
  [CliWrap](https://github.com/Tyrrrz/CliWrap)) in a per-request temp directory, streams the
  downloaded files back as a `multipart/form-data` response, and deletes the temp directory.
- **GalleryDl.McpServer** — stdio MCP server in a second container. Exposes the
  `download_gallery` and `list_resources` tools, forwards requests to the WebApi over the compose
  network, and saves the returned files to a directory chosen by the agent.

```
AI agent ──(MCP/stdio)──> GalleryDl.McpServer ──(HTTP, compose network)──> GalleryDl.WebApi ──> gallery-dl
                                   │                                              │
                                   └── saves files to /downloads (bind mount) <── multipart response
```

## Prebuilt artifacts

Every semver tag (`1.2.3`, no `v` prefix) publishes:

- A multi-arch (amd64/arm64) WebApi image on GHCR:
  `ghcr.io/magicxor/gallerydl-webapi:<version>` (plus `latest`).
- A framework-dependent, arch-neutral `GalleryDl.McpServer-<version>.tar.gz`/`.zip` on
  [GitHub Releases](https://github.com/magicxor/GalleryDlManager/releases) — unpack and run with
  `dotnet GalleryDl.McpServer.dll` on any machine/container with the .NET 10 runtime.

The MCP server is not published as an image: it is a stdio component spawned by an MCP client,
not a standalone service. Its Dockerfile only serves the local docker-compose setup below, which
builds it from source.

## Quick start

```bash
# 1. Build both images
docker compose --profile mcp build

# 2. Start the WebApi (the MCP server is launched on demand by the MCP client)
docker compose up -d gallerydl-webapi
```

Register the MCP server in your client (e.g. `.mcp.json` for Claude Code, `mcp.json` for VS Code):

```json
{
  "mcpServers": {
    "gallerydl": {
      "command": "docker",
      "args": [
        "compose", "-f", "C:/git/personal/GalleryDlManager/docker-compose.yml",
        "run", "--rm", "-T", "gallerydl-mcpserver"
      ]
    }
  }
}
```

`-T` disables TTY allocation so stdout stays clean for MCP JSON-RPC. `docker compose run`
automatically starts the `gallerydl-webapi` dependency if it is not running.

Then ask the agent, for example:

> Download 3 images tagged "dragon" from furry34.com, skipping the first 2, into /downloads/dragons

Files appear on the host under `./downloads/dragons/` (the `/downloads` prefix is bind-mounted).

## Configuration

### WebApi (`src/GalleryDl.WebApi/appsettings.json`, section `GalleryDl`)

| Setting | Meaning | Default |
| --- | --- | --- |
| `Resources` | Map of resource id → `UrlTemplate` with a `{query}` placeholder, plus an optional `TagSeparator` (see below) and the `IsNsfw` / `HasSortFeature` flags | 47 preconfigured sites (12 more commented out as too slow for the 40s timeout) |
| `AllowedExtensions` | Passed to gallery-dl as a `--filter` extension check | jpg, jpeg, png, webp |
| `BlacklistTags` | Passed as `--tags-blacklist` (requires gallery-dl ≥ 1.32) | ai-generated, ... |
| `MaxTake` | Upper bound for the `take` query parameter | 50 |
| `TimeoutSeconds` | gallery-dl execution timeout | 40 |
| `ExtraArgs` | Extra CLI args appended verbatim | `[]` |

Endpoints: `GET /api/resources`, `GET /api/download?resource=&query=&skip=&take=`
(multipart/form-data on success; RFC 7807 problem JSON with 400/404/502/504 on errors).

Results are sorted by score (highest first) on sites that support it (`HasSortFeature: true`).
gallery-dl itself cannot reorder results, so the sort syntax is baked directly into that resource's
`UrlTemplate` (e.g. `...&tags={query}+sort:score:desc` on gelbooru-based sites, `...+order:score` on
moebooru ones). Resources whose template has no sort clause (`HasSortFeature: false`) use the site's
default ordering. `IsNsfw` marks resources that host adult / not-safe-for-work content.

Multiple tags are always passed to the API space-separated (e.g. `query=cat_ears red_coat female`),
with underscores inside multi-word tags. The API re-joins them with each resource's `TagSeparator`
before building the URL — a space for the booru/moebooru/shimmie family (the default) and `|` for the
furry34-family sites (furry34, rule34vault, yiffverse). Callers never need to know the per-site
convention.

`skip`/`take` map to gallery-dl's 1-based inclusive `--range` as `(skip+1)-(skip+take)`. The API
kills gallery-dl as soon as `take` files are downloaded (on album-style sites `--range` applies per
album, so gallery-dl would otherwise keep pulling files from every album in the listing). On
timeout the files fetched so far are returned as a partial result — 504 means nothing was
downloaded in time.

### McpServer (`src/GalleryDl.McpServer/appsettings.json`, section `GalleryDlApi`)

| Setting | Meaning | Default |
| --- | --- | --- |
| `BaseUrl` | WebApi address | `http://gallerydl-webapi:8080` |
| `TimeoutSeconds` | HTTP timeout towards the WebApi | 60 |
| `MaxTake` | Upper bound for the tool's `take` argument (can be stricter than the WebApi's `MaxTake`) | 10 |
| `AllowedPathPrefixes` | Directories `download_gallery` may write under | `/downloads`, `/tmp` |

Path rules for `download_gallery`: the path must be absolute, must not contain `.`/`..` segments,
and must be under an allowed prefix. Existing files are never overwritten (the operation fails
atomically); writing into an existing non-empty directory is fine. Note that only `/downloads` is
bind-mounted to the host — files saved under `/tmp` stay inside the container.

On Linux hosts, make sure `./downloads` is writable by the container user (the image runs as a
non-root user): `chmod 777 downloads` or adjust ownership.

## Development

```bash
dotnet build src/GalleryDlManager.slnx
```

Run the WebApi locally (requires gallery-dl on PATH): `dotnet run --project src/GalleryDl.WebApi`,
then use `src/GalleryDl.WebApi/GalleryDl.WebApi.http` for smoke requests.
See `src/GalleryDl.McpServer/README.md` for running the MCP server against a local WebApi.
