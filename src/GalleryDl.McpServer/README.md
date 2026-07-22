# GalleryDl.McpServer

A stdio MCP server that lets an AI agent download media galleries. It forwards requests to the
`GalleryDl.WebApi` service (which runs [gallery-dl](https://github.com/mikf/gallery-dl)), receives
the downloaded files as a multipart response, and saves them to a local directory.

## Tools

- **`download_gallery`** `(resource, query, path, skip = 0, take = 5)` — downloads media files
  matching `query` from `resource` and saves them into the absolute directory `path`.
  - `path` must be absolute, must not contain `.`/`..` segments, and must be located under one of
    the configured `AllowedPathPrefixes`.
  - Existing files are never overwritten: if any incoming file already exists in `path`, the whole
    operation fails and nothing is written.
- **`list_resources`** `()` — lists the resource ids accepted by `download_gallery`.

## Configuration

`appsettings.json` (overridable via environment variables):

| Setting | Env var | Default |
| --- | --- | --- |
| `GalleryDlApi:BaseUrl` | `GalleryDlApi__BaseUrl` | `http://gallerydl-webapi:8080` |
| `GalleryDlApi:TimeoutMinutes` | `GalleryDlApi__TimeoutMinutes` | `10` |
| `GalleryDlApi:AllowedPathPrefixes` | `GalleryDlApi__AllowedPathPrefixes__0`, ... | `/downloads`, `/tmp` |

## Running

The intended way to run this server is via docker compose from the repository root — see the root
`README.md`. An MCP client launches it with:

```
docker compose -f <repo>/docker-compose.yml run --rm -T gallerydl-mcpserver
```

For local development against a locally running WebApi:

```json
{
  "servers": {
    "gallerydl": {
      "type": "stdio",
      "command": "dotnet",
      "args": [ "run", "--project", "<PATH TO>/src/GalleryDl.McpServer" ],
      "env": { "GalleryDlApi__BaseUrl": "http://localhost:5118" }
    }
  }
}
```

All logs go to stderr; stdout is reserved for the MCP JSON-RPC protocol.
