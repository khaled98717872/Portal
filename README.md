# Portal

A multi-tenant portal that hosts several business systems (modules) for public and private organizations.
Built as a **modular monolith** on **.NET 10**, with a Blazor Server shell, a separate OpenID Connect identity server, role-based permissions, per-module audit logging, and per-tenant data isolation.

> Status: foundation and administration are in place (tenancy, authentication, permissions, audit log, tenant/user/role administration). The `Hr` and `Documents` modules are skeletons; `Hr` has a sample `Employee` API that exercises tenant isolation, permissions, and auditing. Localization (Arabic/English with RTL) covers the shell, the sign-in page, and the administration pages.

---

## Table of contents

- [Architecture](#architecture)
- [Solution structure](#solution-structure)
- [Tech stack](#tech-stack)
- [Getting started](#getting-started)
- [Development accounts and sample tenants](#development-accounts-and-sample-tenants)
- [Administration pages](#administration-pages)
- [How it works](#how-it-works)
- [Adding a new module](#adding-a-new-module)
- [Database and migrations](#database-and-migrations)
- [Configuration reference](#configuration-reference)
- [Development notes](#development-notes)
- [Known limitations](#known-limitations)
- [Roadmap](#roadmap)

---

## Architecture

- **Modular monolith.** One deployable host; each business system is an isolated module with its own Domain, Application, Infrastructure, and UI projects. A module can be extracted into a service later.
- **Portal.Host** is the Blazor Server shell: layout, navigation, module registration, and the administration pages. The menu shows only the modules enabled for the signed-in tenant and the admin pages the user may access.
- **Portal.Identity** is a separate OpenID Connect server (OpenIddict, Authorization Code + PKCE for sign-in, Client Credentials for the internal admin API). Every user belongs to exactly one tenant, and tokens carry `tenant_id`, `role`, and `permission` claims.
- **Multi-tenancy**, two isolation modes per tenant:
  - `SharedDatabase`: shared database, `TenantId` column, EF Core global query filter.
  - `DedicatedDatabase`: separate database per tenant (for sensitive government bodies).
- **Platform tenant.** A built-in tenant (`platform`) hosts the platform administrators. It has no business modules and cannot be edited from the UI.
- **Module access** is controlled per tenant (subscriptions, optional expiry) and enforced on API routes and page routes.
- **Permissions** are role-based and checked per endpoint: `module.resource.action`.
- **Audit log**: every create, update, and delete of tenant-scoped data is recorded in the same transaction, in the module's own schema.
- **Data layout:** one PostgreSQL schema per module, no foreign keys across schemas, independent migrations per module.
- **Modules do not call each other directly.** They communicate through `Contracts` projects (interfaces) and, later, integration events.

```
Users --> Portal.Host (Blazor Server shell + admin pages) --> Modules (Hr, Documents, ...)
              |                                                  |
              |-- Portal.Tenancy (tenants, subscriptions, EF tenant filter, audit)
              |-- Portal.SharedKernel (abstractions, permissions)
              '-- Portal.Identity (OIDC sign-in, admin API) --> portal_identity DB
Modules --> portal_shared DB (TenantId filter)  or  dedicated DB per tenant
```

## Solution structure

```
Portal/
  src/
    Portal.Host/
      Admin/                      Identity admin client and user/role services
      Components/Layout/          NavMenu, ModuleMenu, LanguageSwitcher
      Components/Pages/Admin/     Tenants, Users, Roles pages
      Resources/                  SharedResource.ar.resx (Arabic UI texts)
    Portal.Identity/
      Data/                       IdentityDb, AppUser, seeders (users, roles, OIDC clients)
      Pages/Account/              Login page
      Resources/                  SharedResource.ar.resx (Arabic sign-in texts)
      AdminApi.cs                 /api/admin endpoints (users, roles)
    Portal.SharedKernel/
      Authorization/              Permissions, wildcard matcher, policies
      Auditing/                   AuditEntry, ICurrentUser, [AuditIgnore]
      Modules/                    IModule
      Tenancy/                    ITenantContext, ITenantScoped, platform tenant
      Localization/               Supported cultures, culture endpoint
    Portal.Tenancy/
      Data/                       TenancyDb, TenantDbContext (filter + audit), audit endpoint
      DbTenantStore, ModuleAccess, TenantAdminService, tenant resolution middleware
    Modules/
      Hr/
        Portal.Modules.Hr.Contracts/        HrPermissions
        Portal.Modules.Hr.Domain/
        Portal.Modules.Hr.Application/
        Portal.Modules.Hr.Infrastructure/   HrModule, HrDbContext, migrations
        Portal.Modules.Hr.UI/               Razor class library
      Documents/            (same layout)
  tests/
```

**Dependency direction**

- `Domain` depends only on `SharedKernel`.
- `Application` depends on `Domain`, `Contracts`, `SharedKernel`.
- `Infrastructure` depends on `Application` and `Tenancy`.
- `UI` depends on `Application`.
- Only `Portal.Host` references every module.

## Tech stack

| Area | Choice |
| --- | --- |
| Runtime | .NET 10 |
| UI | Blazor Web App (Server interactivity), Bootstrap |
| Data | EF Core 10 + PostgreSQL (Npgsql) |
| Auth | ASP.NET Core Identity + OpenIddict (OIDC) |
| Tenancy | Custom (`Portal.Tenancy`): claim-based resolution, EF global query filters |
| Localization | `IStringLocalizer` + `.resx` files, Arabic (default) and English, RTL support |

## Getting started

### Prerequisites

- .NET 10 SDK
- PostgreSQL running on `localhost:5432`
- EF Core CLI tool:

```powershell
dotnet tool install --global dotnet-ef   # or: dotnet tool update --global dotnet-ef
```

### 1. Restore and build

```powershell
dotnet build
```

### 2. Configure secrets (never commit passwords)

Replace `<password>` with your PostgreSQL password and use your own random values for the two client secrets.

```powershell
# Identity server
dotnet user-secrets set "ConnectionStrings:Identity" "Host=localhost;Port=5432;Database=portal_identity;Username=postgres;Password=<password>" --project src/Portal.Identity
dotnet user-secrets set "Clients:PortalHost:Secret" "<host-client-secret>" --project src/Portal.Identity
dotnet user-secrets set "Clients:PortalHostAdmin:Secret" "<admin-client-secret>" --project src/Portal.Identity

# Host
dotnet user-secrets set "ConnectionStrings:Shared" "Host=localhost;Port=5432;Database=portal_shared;Username=postgres;Password=<password>" --project src/Portal.Host
dotnet user-secrets set "Oidc:ClientSecret" "<host-client-secret>" --project src/Portal.Host
dotnet user-secrets set "IdentityAdmin:ClientSecret" "<admin-client-secret>" --project src/Portal.Host
dotnet user-secrets set "Seed:MohConnectionString" "Host=localhost;Port=5432;Database=portal_moh;Username=postgres;Password=<password>" --project src/Portal.Host
```

Secrets that must match between the two apps:

| Identity | Host |
| --- | --- |
| `Clients:PortalHost:Secret` | `Oidc:ClientSecret` |
| `Clients:PortalHostAdmin:Secret` | `IdentityAdmin:ClientSecret` |

### 3. Apply migrations

EF creates a database automatically if it does not exist. A `fail:` log line about a missing history table on a fresh database is expected.

```powershell
# Identity (users, roles, OpenIddict tables)
dotnet ef database update --project src/Portal.Identity --startup-project src/Portal.Identity --context IdentityDb

# Tenancy (tenants + enabled modules), lives in the shared database
dotnet ef database update --project src/Portal.Tenancy --startup-project src/Portal.Host --context TenancyDb

# HR module: shared database
dotnet ef database update --project src/Modules/Hr/Portal.Modules.Hr.Infrastructure --startup-project src/Portal.Host --context HrDbContext

# HR module: dedicated database of the sample tenant "moh"
dotnet ef database update --project src/Modules/Hr/Portal.Modules.Hr.Infrastructure --startup-project src/Portal.Host --context HrDbContext `
  --connection "Host=localhost;Port=5432;Database=portal_moh;Username=postgres;Password=<password>"
```

### 4. Run

Use two terminals. The ports are fixed because the Host trusts the Identity server at `http://localhost:5200`.

```powershell
dotnet run --project src/Portal.Identity --urls http://localhost:5200
```

```powershell
dotnet run --project src/Portal.Host --urls http://localhost:5103
```

In Development, the apps seed sample data on startup:

- Identity: roles, users, and the two OIDC clients (`portal-host`, `portal-host-admin`).
- Host: the platform tenant, sample tenants, and their enabled modules.

### 5. Verify

1. Open `http://localhost:5103/login` and sign in (see accounts below).
2. `http://localhost:5103/api/me` shows your claims, including `tenant_id`, `role`, and `permission`.
3. `http://localhost:5103/api/hr/employees` returns only your tenant's data.
4. `http://localhost:5103/api/hr/audit` shows the audit trail (requires `hr.audit.read`).
5. `http://localhost:5103/logout` signs out.

## Development accounts and sample tenants

> Development seed data only. Never ship these credentials.

Password for all accounts: `Passw0rd!Dev1`

| Account | Tenant | Role | Notes |
| --- | --- | --- | --- |
| `platform@portal.test` | `platform` | `PlatformAdmin` | Manages tenants, users of any tenant, and role definitions |
| `admin@moh.test` | `moh` (dedicated database `portal_moh`) | `TenantAdmin` | Modules: Hr, Documents |
| `admin@acme.test` | `acme` (shared database) | `TenantAdmin` | Modules: Hr, Documents |
| `viewer@acme.test` | `acme` | `HrViewer` | Read-only access to employees |
| `admin@beta.test` | `beta` (shared database) | `TenantAdmin` | Module: Hr only |

Identity password policy (ASP.NET Core default): at least 6 characters with an uppercase letter, a lowercase letter, a digit, and a symbol.

## Administration pages

| Page | Who can open it | What it does |
| --- | --- | --- |
| `/admin/tenants` | `platform.tenants.manage` (platform admins) | Create and edit tenants, activate or deactivate them, manage module subscriptions and expiry dates |
| `/admin/users` | `identity.users.manage` (tenant admins, own tenant) or `platform.users.manage` (any tenant) | Create users, assign roles, enable or disable users, reset passwords |
| `/admin/roles` | `platform.roles.manage` (platform admins) | Create roles and choose their permissions |

Rules enforced by the services:

- The `platform` tenant cannot be modified. Tenant identifiers are lowercase letters, digits, and hyphens, and some names are reserved (`platform`, `www`, `api`, `admin`, `identity`).
- A tenant's isolation mode cannot change after creation (existing data would not move).
- A tenant admin only sees and manages users of their own tenant.
- Nobody can modify their own account from the users page.
- Roles that carry `platform.*` permissions can only be assigned to users of the platform tenant.
- `TenantAdmin` and `PlatformAdmin` are system roles and cannot be edited or deleted.
- Tenant and subscription changes take effect immediately (the caches are invalidated).

## How it works

### Authentication

The Host signs users in through OpenID Connect (Authorization Code + PKCE) against `Portal.Identity` and keeps a local cookie session. The tokens carry `tenant_id`, `role`, and `permission` claims. API calls without a session receive `401` or `403` instead of a redirect.

### Tenant resolution

`TenantResolutionMiddleware` runs after authentication:

1. **Authenticated user:** the tenant comes from the `tenant_id` claim only. Any `X-Tenant` header is ignored, so a user cannot switch tenants by editing a header. An inactive or unknown tenant is rejected with `403`.
2. **Unauthenticated, Development only:** the tenant can be selected with the `X-Tenant` header (or a subdomain) for testing.
3. **Production:** only the claim is used.

For Blazor Server, `TenantCircuitHandler` sets the tenant and current user once per circuit from the authentication state, because interactive components do not pass through the HTTP middleware.

### Data isolation

- Entities implement `ITenantScoped`. `TenantDbContext` applies a global query filter on `TenantId`, stamps `TenantId` on insert, and prevents modification of `TenantId`.
- `TenantConnectionResolver` picks the connection string: the tenant's own database for `DedicatedDatabase`, otherwise `ConnectionStrings:Shared`.
- Saving tenant data without a resolved tenant throws.

### Module access (subscriptions)

`ModuleAccessMiddleware` returns `403` when a module is not enabled for the tenant, for both `/api/{module}/...` and `/{module}` page routes. Subscriptions can expire (`ExpiresAt`). The navigation menu lists only enabled modules.

### Permissions

- Format: `module.resource.action`, for example `hr.employees.read`.
- Wildcards: `hr.employees.*`, `hr.*`, and `*`. The `*` wildcard covers every module permission but **never** permissions that start with `platform.`.
- Permissions are granted through roles (role claims) and issued as `permission` claims. A permission change reaches a user on their next sign-in.
- Endpoints require a permission through a policy named after it: `.RequireAuthorization(HrPermissions.Employees.Read)`.
- Every module declares its permissions through `IModule.Permissions`, and policies are registered for all of them at startup.

System roles:

| Role | Permissions |
| --- | --- |
| `TenantAdmin` | `*` (everything in the tenant's modules, including user management) |
| `PlatformAdmin` | `platform.*` (tenants, users of any tenant, role definitions) |

Sample roles seeded in Development: `HrManager` (all employee actions and `hr.audit.read`) and `HrViewer` (`hr.employees.read`).

Built-in permissions:

| Permission | Meaning |
| --- | --- |
| `hr.employees.read`, `.create`, `.update`, `.delete` | Employee actions in the HR module |
| `hr.audit.read` | Read the HR audit log |
| `identity.users.manage` | Manage users of the caller's own tenant |
| `platform.tenants.manage` | Manage tenants and subscriptions |
| `platform.users.manage` | Manage users of any tenant |
| `platform.roles.manage` | Manage role definitions |

### Audit log

- `TenantDbContext` records every create, update, and delete of `ITenantScoped` entities in the **same transaction** as the change.
- Each module has its own `AuditEntries` table in its schema (for example `hr."AuditEntries"`), so a dedicated-database tenant keeps its audit trail in its own database.
- A record holds the tenant, user, time, action, entity type and id, trace id, and the changed fields (old and new values as JSON). Mark a property with `[AuditIgnore]` to exclude it.
- Read it with `GET /api/{module}/audit` (requires `{module}.audit.read`; supports `entityType`, `entityId`, `take`).
- Entities must use client-generated `Guid` keys, because the key must be known before saving.

### Localization

- **Languages:** Arabic (`ar-OM`, the default) and English (`en-GB`), defined once in `Portal.SharedKernel/Localization/PortalLocalization.cs` and used by both the Host and the Identity server.
- **Language selection:** only the `.AspNetCore.Culture` cookie decides the language. It is set by `/culture/set?culture=...&redirectUri=...`, which the language buttons call (sidebar in the Host, link on the sign-in page). The cookie is shared by the Host and the Identity server because both run on the same host name; in production, set the cookie domain so both applications see it.
- **RTL:** `App.razor` sets `lang` and `dir` on `<html>` from the current UI culture, and loads Bootstrap RTL plus `wwwroot/rtl.css` when the culture is right-to-left.
- **Texts:** components use `IStringLocalizer<SharedResource>`, available as `L` in every Host component. The key is the English text and `Resources/SharedResource.ar.resx` holds the Arabic, so a missing translation shows the English text instead of failing.
- **Adding a text:** write `@L["English text"]` in the component and add a `<data name="English text">` entry with the Arabic value to `SharedResource.ar.resx`.
- **Service messages:** the admin pages show errors through `L[ex.Message]`, so fixed messages are translated by adding the exact English message to the resource file. Messages with dynamic parts stay in English.
- **Modules:** module UI projects (Razor class libraries) will use their own `Resources` folder and a marker class per module.

### Identity admin API

`Portal.Identity` exposes `/api/admin` for user and role management. It is called only by the Host.

- Authentication: OAuth client credentials (`portal-host-admin`, scope `identity.admin`).
- The Host authorizes the current user and restricts a tenant admin to their own tenant before calling the API.
- Endpoints: `GET/PUT/DELETE /api/admin/roles[/{name}]`, `GET/POST /api/admin/tenants/{tenantId}/users`, `PUT /api/admin/tenants/{tenantId}/users/{id}`, `POST /api/admin/tenants/{tenantId}/users/{id}/password`.
- Do not expose this API to a public network. Restrict it to the internal network between the Host and the Identity server.

## Adding a new module

1. Create the five projects under `src/Modules/<Name>/` (`Contracts`, `Domain`, `Application`, `Infrastructure`, `UI`) with the same references as `Hr`, and add them to the solution.
2. In `Contracts`, add a `<Name>Permissions` class with the permission names.
3. In `Infrastructure`, add `<Name>Module : IModule`:
   - `Permissions`: declare the module's permissions (with descriptions).
   - `RegisterServices`: register the module's `DbContext` with `services.AddTenantDbContext<TContext>("<schema>")`.
   - `MapEndpoints`: map routes under `/api/<name>` (lowercase module name), call `.RequireAuthorization()` on the group and the specific permission on each endpoint, and add `group.MapAudit<TContext>(<Name>Permissions.Audit.Read)`.
4. Make tenant-owned entities implement `ITenantScoped`, and derive the `DbContext` from `TenantDbContext` (it takes `ITenantContext` and `ICurrentUser`).
5. In `UI`, add a marker class and pages (the first page route should be `/<name>`).
6. Register the module in `Portal.Host/ModuleCatalog.cs`: add it to `Modules`, `UiAssemblies`, and `Menu`.
7. Create the module migration (own schema) and apply it to the shared database and to every dedicated tenant database.
8. Subscribe tenants to the module from `/admin/tenants`, and grant its permissions to roles from `/admin/roles`.

The module name, the API route segment, and the page route segment must match (case-insensitive), because module access is enforced by route.

## Database and migrations

| Database | Schemas | Purpose |
| --- | --- | --- |
| `portal_identity` | `identity` | Users, roles, OpenIddict applications and tokens |
| `portal_shared` | `tenancy`, `hr`, ... | Tenant registry, subscriptions, data and audit of shared-database tenants |
| `portal_moh` (sample) | `hr`, ... | Data and audit of a dedicated-database tenant |

Add a migration (example for HR):

```powershell
dotnet ef migrations add <Name> `
  --project src/Modules/Hr/Portal.Modules.Hr.Infrastructure `
  --startup-project src/Portal.Host `
  --context HrDbContext --output-dir Migrations
```

Every module migration must be applied to the shared database and to each dedicated tenant database. The `tenancy` schema exists only in the shared database. A new dedicated-database tenant needs the module migrations applied to its database manually (automated provisioning is on the roadmap).

## Configuration reference

| Key | App | Purpose |
| --- | --- | --- |
| `ConnectionStrings:Identity` | Identity | Identity database |
| `ConnectionStrings:Shared` | Host | Shared database (also holds the `tenancy` schema) |
| `Clients:PortalHost:Secret` | Identity | OIDC client secret for the Host sign-in |
| `Clients:PortalHost:Url` | Identity | Host base URL used for redirect URIs (default `http://localhost:5103`) |
| `Clients:PortalHostAdmin:Secret` | Identity | Client secret for the admin API caller |
| `Oidc:Authority` | Host | Identity server URL (default `http://localhost:5200`) |
| `Oidc:ClientId` | Host | Default `portal-host` |
| `Oidc:ClientSecret` | Host | Must equal `Clients:PortalHost:Secret` |
| `IdentityAdmin:ClientId` | Host | Default `portal-host-admin` |
| `IdentityAdmin:ClientSecret` | Host | Must equal `Clients:PortalHostAdmin:Secret` |
| `Seed:MohConnectionString` | Host | Dedicated database connection used by Development seeding |

## Development notes

- Stop both apps before `dotnet build` or `dotnet ef`, otherwise the build can fail on locked files.
- After changing roles or permissions, sign out and sign in again to refresh the claims.
- In Blazor components, do not name an injected service the same as the component (for example `@inject UserAdminService Users` inside `Users.razor` fails with CS0542).
- `dotnet ef` needs the module's `Infrastructure` project to build and a startup project that registers the `DbContext` (the Host).

## Known limitations

- **Roles are global**, shared by all tenants. Tenant-specific roles need a `TenantId` on roles and a custom role-name validator.
- **Disabling a user** blocks new sign-ins and token refresh, but an existing Host session cookie stays valid until it expires (up to 8 hours). Deactivating a tenant also does not end open Blazor circuits.
- **In-app navigation inside a Blazor circuit** does not pass through the HTTP middleware, so module access on pages is enforced on full page loads and API calls. Add a page-level module guard before exposing sensitive pages.
- **Tenant connection strings** are stored as plain text in `tenancy."Tenants"` and shown to platform admins. Encrypt them or store a secret reference (for example Key Vault) before production.
- **Development settings** are not production-safe: HTTP transport, OpenIddict development certificates, seeded accounts, and the header/subdomain tenant resolver.
- Service and Identity error messages that contain dynamic parts (a reserved identifier, a missing role name) and the ASP.NET Core Identity password validation messages are shown in English.
- No email invitations or forced password change on first sign-in.
- No automated tests yet.

## Roadmap

- Localization of module UI projects (per-module resource files) and of validation messages
- Tenant-specific roles
- Automated provisioning for dedicated-database tenants
- Integration events with the Outbox pattern; notifications; file storage; background jobs
- Observability (Serilog, OpenTelemetry), health checks, Docker and CI/CD
- Real implementations for the HR and Documents modules; automated tests
