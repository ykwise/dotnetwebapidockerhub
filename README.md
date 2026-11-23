# Secret Verification API (.NET 8 + Docker + GitHub Actions + Docker Hub)

A minimal ASP.NET Core Web API that validates a secret key supplied as a query
parameter against a secret held in an environment variable. The app is
containerized with Docker and published to Docker Hub by a GitHub Actions
workflow.

> Replace `<your-username>` and `<your-repo>` everywhere below with your actual
> GitHub handle and repository name, and `<dockerhub-user>` with your Docker Hub
> username.

---

## 1. Endpoints

| Method | Path                    | Status | Response |
|--------|-------------------------|--------|----------|
| GET    | `/`                     | 200    | `{ "message": "Welcome to the Secret Verification API", ... }` |
| GET    | `/health`               | 200    | `{ "status": "Healthy", "timestampUtc": "..." }` |
| GET    | `/verify?key=<secret>`  | 200    | `{ "message": "Authentication successful" }` |
| GET    | `/verify?key=<wrong>`   | 401    | `{ "error": "Unauthorized", "message": "Invalid secret key" }` |
| GET    | `/verify` (no key)      | 400    | `{ "error": "Bad request", "message": "Query parameter 'key' is required..." }` |
| GET    | `/verify` (server has no secret) | 500 | `{ "error": "Server misconfiguration", ... }` |

The secret is read only via `Environment.GetEnvironmentVariable("API_SECRET_KEY")`.
It is never hardcoded in C# and never committed.

---

## 2. Project structure

```
.
├── .github/
│   └── workflows/
│       └── docker-build.yml     # CI: build on PR, build + push to Docker Hub on main
├── SecretApi/
│   ├── Properties/
│   │   └── launchSettings.json
│   ├── Program.cs               # endpoints + .env loader + constant-time compare
│   ├── SecretApi.csproj
│   ├── appsettings.json
│   └── appsettings.Development.json
├── .dockerignore
├── .env                         # LOCAL ONLY - git-ignored, never committed
├── .env.example                 # variable name only, no real secret
├── .gitignore
├── Dockerfile
└── README.md
```

---

## 3. Run locally with the .NET SDK

Prerequisite: .NET SDK 8.0 or later.

```bash
# 1. Create your local secret file from the template
cp .env.example .env
#    then edit .env and set, for example:
#    API_SECRET_KEY=my-secret-key

# 2. Run the API (the app loads .env from the working directory)
cd SecretApi
dotnet run
```

The API starts on `http://localhost:5080`.

Alternatively, set the variable in the shell instead of using a `.env` file:

```bash
# Linux / macOS
export API_SECRET_KEY=my-secret-key

# Windows PowerShell
$env:API_SECRET_KEY = "my-secret-key"
```

### Test the endpoints

```bash
curl http://localhost:5080/
curl http://localhost:5080/health
curl -i "http://localhost:5080/verify?key=my-secret-key"   # 200
curl -i "http://localhost:5080/verify?key=wrong-key"       # 401
```

---

## 4. Run with Docker

```bash
docker build -t secret-api:local .

docker run --rm -p 8080:8080 \
  -e API_SECRET_KEY=my-secret-key \
  --name secret-api secret-api:local
```

Or feed the whole `.env` file in (the file stays on your machine, it is never
baked into the image):

```bash
docker run --rm -p 8080:8080 --env-file .env --name secret-api secret-api:local
```

### Test

```bash
curl http://localhost:8080/
curl http://localhost:8080/health
curl -i "http://localhost:8080/verify?key=my-secret-key"   # 200
curl -i "http://localhost:8080/verify?key=wrong-key"       # 401
```

The Dockerfile uses `mcr.microsoft.com/dotnet/sdk:8.0` to restore, build and
publish, then copies only the published output into the smaller
`mcr.microsoft.com/dotnet/aspnet:8.0` runtime image. Kestrel listens on port
`8080` inside the container via `ASPNETCORE_URLS=http://+:8080`.

---

## 5. Push the project to GitHub

```bash
git init
git add .
git commit -m "ASP.NET Core API with Docker and GitHub Actions"
git branch -M main
git remote add origin https://github.com/<your-username>/<your-repo>.git
git push -u origin main
```

Confirm `.env` was not committed:

```bash
git ls-files | grep -c "^\.env$"   # must print 0
```

---

## 6. CI setup (one-time)

The workflow pushes to Docker Hub, so two repository secrets are required.

1. Create a Docker Hub access token: hub.docker.com > Account Settings >
   Personal access tokens > Generate new token, with Read & Write permission.
2. In the GitHub repo: Settings > Secrets and variables > Actions >
   New repository secret, and add:
   - `DOCKERHUB_USERNAME` = your Docker Hub username
   - `DOCKERHUB_TOKEN` = the token from step 1

Without these two secrets the login step fails and no image is published.

### Workflow behaviour

File: `.github/workflows/docker-build.yml`

| Trigger | What happens |
|---------|--------------|
| Pull request into `main` | Build the image only. **No push.** |
| Push to `main` | Build and push `<dockerhub-user>/secret-api:latest`. |

Steps: checkout, set up Docker Buildx, log in to Docker Hub with the stored
token, then build and push with `push:` evaluated from the event type.

---

## 7. Pull and run the published image

```bash
docker pull <dockerhub-user>/secret-api:latest

docker run --rm -p 8080:8080 \
  -e API_SECRET_KEY=my-secret-key \
  <dockerhub-user>/secret-api:latest

curl -i "http://localhost:8080/verify?key=my-secret-key"
```

Docker Hub repositories are public by default, so no login is needed to pull.

---

## 8. Submission checklist

- [ ] Repository URL: `https://github.com/<your-username>/<your-repo>`
- [ ] Image URL: `https://hub.docker.com/r/<dockerhub-user>/secret-api`
- [ ] `GET /` and `GET /verify` working, `GET /health` added (bonus)
- [ ] 200 / 400 / 401 status codes verified
- [ ] `.env` git-ignored, `.env.example` committed
- [ ] Local `docker build` and `docker run` successful
- [ ] Actions run green on `main`
- [ ] Image pulled and run on a second machine

---

## 9. Publishing to GHCR instead

To target GitHub Container Registry, replace the last two steps of the workflow
with the block below and add `permissions: { contents: read, packages: write }`
to the job. No secrets need to be created; `GITHUB_TOKEN` is automatic.

```yaml
      - run: echo "IMAGE=ghcr.io/${GITHUB_REPOSITORY,,}" >> $GITHUB_ENV
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - uses: docker/build-push-action@v6
        with:
          context: .
          push: ${{ github.event_name != 'pull_request' }}
          tags: ${{ env.IMAGE }}:latest
```

---

## 10. Using a different .NET version

The project targets `net8.0`. To move to another LTS, change two places:

1. `SecretApi/SecretApi.csproj` > `<TargetFramework>net10.0</TargetFramework>`
2. `Dockerfile` > `sdk:10.0` and `aspnet:10.0`
