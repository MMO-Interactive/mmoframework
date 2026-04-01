# Sandbox MMO Wiki (PHP)

A lightweight wiki-style PHP website that renders live documentation from the Management Dashboard APIs.

## Features

- Overview counters from gameplay definitions, live sessions, and crafting recipes.
- API health panel for:
  - `/api/gameplay-definitions`
  - `/api/dashboard`
  - `/api/crafting/recipes`
  - `/api/analytics/overview`
- Wiki sections for zones, items, skills, resources/nodes, crafting, operations, and analytics.

## Configuration

Set the dashboard base URL with:

```bash
export MMO_DASHBOARD_BASE_URL="http://127.0.0.1:8081"
```

Default: `http://127.0.0.1:8081`

## Run locally

```bash
php -S 127.0.0.1:8090 -t Website/Wiki
```

Then open:

- http://127.0.0.1:8090/

## Notes

- This app is read-only and intended for documentation/operations visibility.
- If an API endpoint is unavailable, the wiki will continue rendering and show endpoint-specific errors.
