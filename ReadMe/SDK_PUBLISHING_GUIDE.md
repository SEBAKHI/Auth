# Auth.Sdk — NuGet Publishing & Versioning Guide

This guide covers how to name, version, pack, and publish `Auth.Sdk` as a NuGet package — both to a **private feed** and to **public nuget.org**.

---

## 1. Package Naming

### Conventions

| Property | Value | Why |
|----------|-------|-----|
| **PackageId** | `AuthSystem.Sdk` | Prefix with organization name to avoid collisions on public feeds |
| **Root Namespace** | `Auth.Sdk` | Stays clean for consumers — `using Auth.Sdk.Extensions;` |
| **Assembly Name** | `Auth.Sdk` | Matches the project name |

> **Rule:** On nuget.org, package IDs are globally unique. Always prefix with your organization name (`AuthSystem.Sdk`, not `Auth.Sdk`) to reserve your namespace. On private feeds you have more flexibility, but consistent naming is still recommended.

### NuGet Package ID Prefix Reservation

If publishing to nuget.org, reserve your prefix to prevent impersonation:

1. Go to https://www.nuget.org/account/manage → Package ID Prefix Reservation
2. Reserve `AuthSystem.` for your account/organization
3. Reserved packages show a verified checkmark on nuget.org

---

## 2. Versioning (SemVer 2.0)

Auth.Sdk follows [Semantic Versioning 2.0.0](https://semver.org/):

```
MAJOR.MINOR.PATCH[-prerelease]
```

### When to Bump

| Change Type | Version Bump | Examples |
|-------------|-------------|----------|
| **Patch** (`1.0.x`) | Bug fixes, no API changes | Fix cache key collision, fix claim mapping bug |
| **Minor** (`1.x.0`) | New APIs added, old APIs deprecated (not removed) | Add `AddAuthSystemAuthentication` overload, add new auth scheme |
| **Major** (`x.0.0`) | Breaking changes — remove deprecated APIs, rename public types, change method signatures | Remove `[Obsolete]` members, change `AuthSystemOptions` shape |

### Pre-release Versions

Use pre-release suffixes for testing before a stable release:

```
1.0.0-alpha.1    ← early development, unstable
1.0.0-beta.1     ← feature-complete, testing
1.0.0-rc.1       ← release candidate, final testing
1.0.0            ← stable release
```

### Deprecation Cycle (for Breaking Changes)

Since some consumers may use `ProjectReference` (always getting latest source), follow a deprecation cycle before removing APIs:

```
v1.0.0  →  v1.1.0 (deprecate old API)  →  v2.0.0 (remove deprecated API)
```

**Step 1 — Deprecate in a minor release:**

```csharp
[Obsolete("Use AddAuthSystemAuthentication(Action<AuthSystemOptions>) instead. Will be removed in v2.0.0.")]
public static void AddAuthSystem(this IServiceCollection services, string baseUrl)
{
    // keep working, delegate to new API
    AddAuthSystemAuthentication(services, opts => opts.BaseUrl = baseUrl);
}
```

**Step 2 — Remove in the next major release:**

```csharp
// v2.0.0 — method removed, consumers had one full minor version to migrate
```

> **Rule:** Never remove or rename a public API without first deprecating it in a minor version. Consumers see `CS0618` compiler warnings guiding them to the replacement.

### Where the Version Lives

The version is set in `Auth.Sdk.csproj`:

```xml
<Version>1.0.0</Version>
```

To bump it, change this single value. All NuGet metadata (`PackageVersion`, `AssemblyVersion`, `FileVersion`) derive from it automatically.

---

## 3. Preparing the .csproj for Publishing

Before publishing, add full package metadata to `Auth.Sdk.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <!-- Package Identity -->
    <PackageId>AuthSystem.Sdk</PackageId>
    <Version>1.0.0</Version>
    <Authors>AuthSystem Contributors</Authors>
    <Company>AuthSystem</Company>
    <Description>SDK for integrating external .NET applications with the AuthSystem. Provides JWT, API Key, and Webhook Key authentication handlers with permission-based authorization.</Description>

    <!-- Package Metadata -->
    <PackageTags>authentication;authorization;jwt;apikey;webhook;sdk</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/your-org/your-repo</PackageProjectUrl>
    <RepositoryUrl>https://github.com/your-org/your-repo</RepositoryUrl>
    <RepositoryType>git</RepositoryType>

    <!-- Package Content -->
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageIcon>icon.png</PackageIcon>

    <!-- Build Settings -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>

  <!-- Include README and icon in the package -->
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
    <None Include="icon.png" Pack="true" PackagePath="\" Condition="Exists('icon.png')" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.0" />
  </ItemGroup>

</Project>
```

### Property Reference

| Property | Required | Description |
|----------|----------|-------------|
| `PackageId` | Yes | Unique identifier on the NuGet feed |
| `Version` | Yes | SemVer version string |
| `Authors` | Yes | Comma-separated author names |
| `Description` | Yes | Short description shown on NuGet gallery |
| `PackageLicenseExpression` | Recommended | SPDX license identifier (e.g., `MIT`, `Apache-2.0`) |
| `PackageTags` | Recommended | Semicolon-separated search tags |
| `RepositoryUrl` | Recommended | Source code URL — enables "Source Link" on nuget.org |
| `PackageReadmeFile` | Recommended | README shown on the package page |
| `GenerateDocumentationFile` | Recommended | Generates XML docs for IntelliSense in consuming projects |

### Package README

Create a `README.md` **inside the Auth.Sdk project folder** (not the repo-level ReadMe folder). This is the README shown on the NuGet package page:

```markdown
# Auth.Sdk

SDK for integrating .NET applications with the AuthSystem.

## Features

- JWT Bearer authentication (JWKS-based, zero network cost per request)
- API Key authentication (X-Api-Key header)
- Webhook Key authentication (?whk= query parameter)
- Permission-based authorization with wildcard support

## Quick Start

services.AddAuthSystemAuthentication(options =>
{
    options.BaseUrl = "https://auth.example.com";
    options.Issuer = "auth-system";
    options.Audience = "auth-api";
    options.GatewayToken = "your-gateway-token";
});

See the full integration guide for details.
```

---

## 4. Building the Package

### Pack Locally

```bash
# From the Auth.Sdk project directory
dotnet pack -c Release

# Output: bin/Release/AuthSystem.Sdk.1.0.0.nupkg
```

### Pack with a Specific Version (Override)

```bash
dotnet pack -c Release -p:Version=1.1.0-beta.1
```

### Inspect the Package

```bash
# List package contents
dotnet nuget locals all -l
# Or use NuGet Package Explorer (GUI tool)
```

---

## 5. Publishing to a Private NuGet Feed

### Option A: Azure DevOps Artifacts

**1. Create a feed:**

- Go to Azure DevOps → Artifacts → Create Feed
- Name: `authsystem-packages`
- Visibility: Organization or specific project

**2. Add the feed as a NuGet source:**

```bash
# Get a PAT (Personal Access Token) with Packaging > Read & Write scope
dotnet nuget add source "https://pkgs.dev.azure.com/{org}/{project}/_packaging/{feed}/nuget/v3/index.json" \
  --name "AuthSystemFeed" \
  --username "az" \
  --password "{PAT}" \
  --store-password-in-clear-text
```

**3. Push the package:**

```bash
dotnet nuget push bin/Release/AuthSystem.Sdk.1.0.0.nupkg \
  --source "AuthSystemFeed" \
  --api-key az
```

**4. Consumer setup — add `nuget.config` to the consuming solution root:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="AuthSystemFeed" value="https://pkgs.dev.azure.com/{org}/{project}/_packaging/{feed}/nuget/v3/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <AuthSystemFeed>
      <add key="Username" value="az" />
      <add key="ClearTextPassword" value="%AZURE_DEVOPS_PAT%" />
    </AuthSystemFeed>
  </packageSourceCredentials>
</configuration>
```

---

### Option B: GitHub Packages

**1. Authenticate:**

```bash
dotnet nuget add source "https://nuget.pkg.github.com/{owner}/index.json" \
  --name "GitHubPackages" \
  --username "{github-username}" \
  --password "{github-PAT}" \
  --store-password-in-clear-text
```

> The PAT needs `write:packages` and `read:packages` scopes.

**2. Push:**

```bash
dotnet nuget push bin/Release/AuthSystem.Sdk.1.0.0.nupkg \
  --source "GitHubPackages"
```

**3. Consumer setup — `nuget.config`:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="GitHubPackages" value="https://nuget.pkg.github.com/{owner}/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <GitHubPackages>
      <add key="Username" value="{github-username}" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </GitHubPackages>
  </packageSourceCredentials>
</configuration>
```

---

### Option C: Self-Hosted (BaGet)

[BaGet](https://loic-sharma.github.io/BaGet/) is a lightweight, open-source NuGet server you can host on your own infrastructure.

**1. Run BaGet via Docker:**

```bash
docker run -d \
  --name baget \
  -p 5555:80 \
  -e "ApiKey=your-api-key-here" \
  -v baget-data:/var/baget \
  loicsharma/baget:latest
```

**2. Add source and push:**

```bash
dotnet nuget add source "http://localhost:5555/v3/index.json" --name "BaGet"

dotnet nuget push bin/Release/AuthSystem.Sdk.1.0.0.nupkg \
  --source "BaGet" \
  --api-key "your-api-key-here"
```

**3. Consumer setup — `nuget.config`:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="BaGet" value="http://your-server:5555/v3/index.json" />
  </packageSources>
</configuration>
```

---

## 6. Publishing to Public nuget.org

### One-Time Setup

1. Create an account at https://www.nuget.org
2. Go to API Keys → Create
   - Package Owner: your account
   - Glob Pattern: `AuthSystem.*`
   - Scopes: Push
3. Copy the API key (shown only once)

### Push

```bash
dotnet nuget push bin/Release/AuthSystem.Sdk.1.0.0.nupkg \
  --source "https://api.nuget.org/v3/index.json" \
  --api-key "{your-nuget-api-key}"
```

> Packages on nuget.org are **immutable** — you cannot overwrite a published version. If you publish `1.0.0`, you must bump to `1.0.1` for the next release. You can unlist (hide) a version, but never delete it.

### Consumer Usage

```xml
<!-- YourApp.csproj -->
<ItemGroup>
  <PackageReference Include="AuthSystem.Sdk" Version="1.0.0" />
</ItemGroup>
```

---

## 7. Consumer Migration (ProjectReference → PackageReference)

When the SDK is published as a NuGet package, consumers switch from:

```xml
<!-- Before: ProjectReference (always gets latest source) -->
<ItemGroup>
  <ProjectReference Include="..\Auth.Sdk\Auth.Sdk.csproj" />
</ItemGroup>
```

To:

```xml
<!-- After: PackageReference (pinned version, upgrade on your schedule) -->
<ItemGroup>
  <PackageReference Include="AuthSystem.Sdk" Version="1.0.0" />
</ItemGroup>
```

### Version Pinning Strategies

| Syntax | Behavior |
|--------|----------|
| `Version="1.0.0"` | Minimum version — accepts 1.0.0 or higher (NuGet default) |
| `Version="[1.0.0]"` | Exact version — only 1.0.0, no upgrades |
| `Version="[1.0.0, 2.0.0)"` | Range — accepts 1.x.x but not 2.0.0+ |

> **Recommendation:** Use `Version="1.0.0"` (default) during development. Use `Version="[1.0.0, 2.0.0)"` in production to accept patches and minor updates but block breaking major changes.

---

## 8. CI/CD Automation

### GitHub Actions — Pack & Publish on Git Tag

Create `.github/workflows/publish-sdk.yml`:

```yaml
name: Publish Auth.Sdk

on:
  push:
    tags:
      - 'sdk-v*'  # Trigger on tags like sdk-v1.0.0, sdk-v1.1.0-beta.1

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Extract version from tag
        id: version
        run: echo "VERSION=${GITHUB_REF_NAME#sdk-v}" >> $GITHUB_OUTPUT

      - name: Pack
        run: dotnet pack Auth/Auth.Sdk/Auth.Sdk.csproj -c Release -p:Version=${{ steps.version.outputs.VERSION }}

      - name: Push to NuGet
        run: dotnet nuget push Auth/Auth.Sdk/bin/Release/*.nupkg --source "https://api.nuget.org/v3/index.json" --api-key ${{ secrets.NUGET_API_KEY }}

      # Optional: also push to private feed
      # - name: Push to Private Feed
      #   run: dotnet nuget push Auth/Auth.Sdk/bin/Release/*.nupkg --source "AuthSystemFeed" --api-key ${{ secrets.AZURE_DEVOPS_PAT }}
```

### Release Workflow

```bash
# 1. Update version in Auth.Sdk.csproj
#    <Version>1.1.0</Version>

# 2. Commit
git add Auth/Auth.Sdk/Auth.Sdk.csproj
git commit -m "Bump Auth.Sdk to v1.1.0"

# 3. Tag
git tag sdk-v1.1.0

# 4. Push (triggers CI/CD)
git push origin main --tags
```

### Azure DevOps Pipeline

```yaml
trigger:
  tags:
    include:
      - sdk-v*

pool:
  vmImage: 'ubuntu-latest'

steps:
  - task: UseDotNet@2
    inputs:
      version: '10.0.x'

  - script: |
      VERSION=$(echo $BUILD_SOURCEBRANCH | sed 's|refs/tags/sdk-v||')
      dotnet pack Auth/Auth.Sdk/Auth.Sdk.csproj -c Release -p:Version=$VERSION
    displayName: 'Pack'

  - task: NuGetCommand@2
    inputs:
      command: 'push'
      packagesToPush: 'Auth/Auth.Sdk/bin/Release/*.nupkg'
      nuGetFeedType: 'internal'
      publishVstsFeed: '{project}/{feed}'
```

---

## 9. Quick Reference

### Common Commands

| Task | Command |
|------|---------|
| Pack | `dotnet pack -c Release` |
| Pack with version | `dotnet pack -c Release -p:Version=1.1.0` |
| Pack pre-release | `dotnet pack -c Release -p:Version=1.1.0-beta.1` |
| Push to nuget.org | `dotnet nuget push *.nupkg --source nuget.org --api-key {key}` |
| Push to private feed | `dotnet nuget push *.nupkg --source "FeedName" --api-key {key}` |
| List sources | `dotnet nuget list source` |
| Add source | `dotnet nuget add source {url} --name {name}` |
| Remove source | `dotnet nuget remove source {name}` |

### Version Decision Tree

```
Is the change backward-compatible?
├── Yes → Does it add new public APIs?
│   ├── Yes → Bump MINOR (1.0.0 → 1.1.0)
│   └── No  → Bump PATCH (1.0.0 → 1.0.1)
└── No  → Have you deprecated the old API in a prior minor release?
    ├── Yes → Bump MAJOR (1.x.x → 2.0.0)
    └── No  → First deprecate in a MINOR release, then bump MAJOR in the next release
```

### Checklist Before Publishing

- [ ] Version bumped in `Auth.Sdk.csproj`
- [ ] `dotnet build -c Release` passes with no errors
- [ ] `dotnet pack -c Release` produces `.nupkg` successfully
- [ ] Breaking changes documented in release notes
- [ ] Deprecated APIs marked with `[Obsolete]` (if applicable)
- [ ] README.md inside Auth.Sdk project folder is up to date
- [ ] Git tag created (`sdk-v{version}`)
