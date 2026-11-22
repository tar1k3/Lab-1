# Модель загроз SecureLab, ЛР 6

## Межі огляду

Локальний стенд: browser client (`src/SecureLab.Api/Client`), ASP.NET Core API, PostgreSQL у Docker Compose
(`127.0.0.1:54329`), конфігурація, журнали (console sink) і NuGet-залежності. Лише штучні акаунти
alice, bob (Reporter), morgan (Analyst), admin (Administrator).

**Поза межами:** production host, реальний TLS/HSTS, reverse proxy, зовнішній penetration test, Redis,
CI, повна історія Git.

## Активи

| Актив | Чому цінний |
|---|---|
| credentials і authentication cookie `.AspNetCore.Identity.Application` | дають дії від імені користувача |
| власні інциденти Reporter | конфіденційність і цілісність даних користувача |
| привілейований перехід статусу (triage) | лише Analyst; зміна стану workflow |
| internal comments | доступні лише Analyst |
| audit-записи (`SecureLab.Audit`, `incident_status_history`) | відстежуваність дій |
| конфігураційні секрети (`SeedUsers__Password`, connection string) | доступ до БД і seed-акаунтів |

## Змінювальні entry points

register, login, logout, створення (`POST /api/incidents`), оновлення (`PUT`), видалення (`DELETE`)
інциденту і triage (`POST /api/incidents/{id}/triage`). Повний периметр — таблиця 4 звіту.

## Межі довіри

| Межа | Очікувана властивість | Перевірка |
|---|---|---|
| browser → API | автентифікація cookie, авторизація за власником/роллю, antiforgery для змін, CORS лише для 4173 у Development | `AuthorizationTests`, `CsrfRegressionTests`, REG-CORS-01 |
| API → PostgreSQL | параметризовані запити, allowlist сортування, мінімальні дані в audit | `S02_SqlPayload_…`, REG-AUDIT-01 |
| repository → build | lock-файли, відсутність відомих advisories, відсутність реальних секретів у tracked files | `dotnet package list --vulnerable`, `git grep` (рис. 34–35) |
| environment → configuration | Production не читає `appsettings.Development.json`, reset заборонено, OpenAPI не мапиться | `ProductionProfileTests`, рис. 5–7 |
