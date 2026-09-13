# A\*za_Watchdog

Watches the prices of [alza.sk](https://www.alza.sk) products and remembers every
change, so you can tell a real discount from a fake one.

- Paste a product link and it lands on a watch list.
- A background worker re-checks every product on a schedule (every 6 hours by
  default) and records each price change — including AlzaPlus+ and discount-code
  prices.
- Each product shows its price history as a chart, with the all-time low, high and
  median.
- No sign-up, no password: your account is a private URL. Bookmark it — lose the
  link and the account is gone. Anyone with the link can see and edit your lists.

## Tech stack

- **Backend** — ASP.NET Core 10 minimal API, Entity Framework Core, AngleSharp for
  parsing product pages
- **Database** — SQLite for local development, MariaDB in containers
- **Frontend** — Angular 21 (standalone components, signals, zoneless)
- **Deployment** — Docker / Podman Compose, the UI served by nginx

## Running it

### With containers

```bash
podman-compose up -d --build        # or: docker compose up -d --build
```

Open <http://localhost:8081>. This starts MariaDB, the API and the web UI; the
database is created and migrated on startup. Passwords can be overridden by copying
`.env.example` to `.env`.

### Locally

Requires the .NET 10 SDK and Node.js.

```bash
dotnet run --project backend/AlzaWatchdog.Api       # API on http://localhost:5080
cd frontend/alza-watchdog-web && npm install && npm start   # UI on http://localhost:4200
```

The API uses a SQLite file by default, so nothing else needs to be installed.

### Admin section

Put your account key (the id from your URL) into `Admin:Keys` — in
`appsettings.json`, or as `Admin__Keys__0` in `docker-compose.yml` — to unlock an
admin page with all accounts, products, worker status and backup/restore.

## Tests

```bash
dotnet test
```
