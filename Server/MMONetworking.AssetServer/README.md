# MMONetworking.AssetServer

Standalone asset server for Unity AssetBundle delivery and version management.

## Run

```bash
dotnet run --project Server/MMONetworking.AssetServer/MMONetworking.AssetServer.csproj
```

Environment variables:

- `MMO_ASSET_HTTP_PORT` (default `7095`)
- `MMO_ASSET_DATA_ROOT` (default `<repo>/Documents/AssetServerData`)
- `MMO_ASSET_DB` (default `<data_root>/AssetServer.db`)
- `MMO_ASSET_MAX_UPLOAD_BYTES` (default `536870912`, i.e. 512 MB)
- `MMO_ASSET_WRITE_API_KEY` (optional; when set, required in `X-Asset-Api-Key` for all write endpoints)

## API (MVP)

- `POST /api/assets/artifacts?sourceName=...&expectedHash=...` upload raw bundle bytes (optional hash verification)
- `GET /api/assets/artifacts/{hash}` download by immutable hash
- `GET /api/assets/artifacts/{hash}/meta` get artifact metadata by hash
- `POST /api/assets/bundles/{bundleName}/versions` register version metadata
- `GET /api/assets/bundles?assetType=zone` list bundles (filter by type)
- `POST /api/assets/manifests` create manifest container
- `POST /api/assets/manifests/{manifestId}/entries` assign bundle versions
- `POST /api/assets/channels/{channel}/promote` set active manifest for channel
- `GET /api/assets/channels/{channel}/manifest?platform=...` resolve client manifest
- `GET /api/assets/channels` list promoted channel heads
- `GET /api/assets/channels/{channel}/history` list promotion history for a channel
- `GET /api/assets/manifests/{manifestId}` inspect a manifest with expanded entries
- `GET /api/assets/stats` aggregate counts/size stats for artifacts, bundle versions, manifests, and channels

All artifact content is stored immutably under `artifacts/<sha256>`.

For zone bundles, submit `assetType: "zone"` when creating bundle versions.
