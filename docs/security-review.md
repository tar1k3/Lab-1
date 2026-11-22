# Security review SecureLab, ЛР 6

Рівень: добрий («Добре»).

## 1. Executive summary

Огляд накопиченого fixed state ЛР 1–5. Виявлено й усунуто три знахідки: F-03 (P1) — пошук інцидентів
був доступний анонімно й без перевірки власника; F-01 (P2) — відповідь 500 надсилалась без заголовків
безпеки; F-02 (P2) — дозволений triage не залишав audit trail. Для всіх cookie-автентифікованих
змінювальних маршрутів додано antiforgery, для пошуку — rate limit. Усі регресійні тести проходять,
відомих advisories у залежностях немає. Тимчасово прийнятих ризиків немає.

## 2. Scope та exclusions

Див. `docs/threat-model.md`, розділ «Межі огляду».

## 3. System overview і trust boundaries

Browser client і API на одному origin `http://localhost:5080`, PostgreSQL у Docker Compose на loopback.
Межі довіри та очікувані властивості — `docs/threat-model.md`.

## 4. Methodology та test environment

- Review-start commit: `21694a0` (v0.5.0 + SDK-політика `10.0.100 / latestFeature`).
- SDK 10.0.401, Windows, Git Bash, Opera GX (Chromium), Docker Desktop.
- Development (`dotnet run`) і production-like profile (`--no-launch-profile -c Release`, `ASPNETCORE_ENVIRONMENT=Production`).
- Ручні перевірки curl і DevTools, автоматичні — `dotnet test SecureLab.sln -c Release` після reset.

## 5. Findings summary

| ID | Finding | Priority | Status | Fixed commit |
|---|---|---|---|---|
| F-03 | пошук анонімний і без перевірки власника | P1 | Fixed | `80c27e2` |
| F-01 | відповідь 500 без заголовків безпеки | P2 | Fixed | `32f0b8b` |
| F-02 | дозволений triage без audit trail | P2 | Fixed | `024205e` |

## 6. Detailed findings

- `docs/findings/F-01-error-headers.md`
- `docs/findings/F-02-triage-audit-trail.md`
- `docs/findings/F-03-search-bola.md`

## 7. Regression results

| ID | Тест / процедура | Result |
|---|---|---|
| T-02 | `Post_InvalidDto_Returns400`, `Post_DuplicateTitle_Returns409` | pass |
| S-02 | `S02_SqlPayload_IsTreatedAsText_AndSortByIsAllowlisted` | pass |
| A-01…A-07 | `SecurityScenarioTests` | pass |
| A-02…A-12 | `AuthorizationTests` | pass |
| REG-XSS-01 | `ClientScript_DoesNotUseDangerousInnerHtmlSink` + ручна процедура ЛР 5 | pass |
| REG-CORS-01 | ручна процедура, рис. 16–17, табл. 3 | pass |
| REG-CSRF-01 | `RegCsrf01_…`, `TokenOfAlice_IsRejectedForBob`, `Perimeter_WithoutToken_…` | pass |
| REG-AUDIT-01 | `RegAudit01_DeniedTriage_NoRecord_AllowedTriage_ExactlyOneRecord` | pass |
| F-01 | `UnexpectedError_KeepsSecurityHeaders` | pass |
| F-03 | `Search_Anonymous_Returns401` | pass |
| RateLimit | `RateLimit_Login_…`, `Search_QuotaPerUser_…` | pass |

## 8. Hardening controls

| Control | Risk | Verification | Boundary |
|---|---|---|---|
| antiforgery (`X-CSRF-TOKEN` + cookie `securelab-af`, `AntiforgeryFilter`) на SC-01…SC-05 | CSRF зі сторонньої сторінки з cookie жертви | REG-CSRF-01 | login/register не захищені токеном — residual risk |
| заголовки безпеки через `Response.OnStarting` | MIME sniffing, витік referrer, clickjacking, також на 500 | рис. 11–15, F-01 test | не замінюють safe DOM і авторизацію |
| CSP `default-src 'self'; … frame-ancestors 'none'` | ін'єкція сторонніх ресурсів, inline-скриптів | ЛР 5, рис. 54–55 | виняток `/scalar` у Development |
| CORS `Lab05BrowserProbe` лише Development, один origin 4173 | читання відповідей чужими origins | REG-CORS-01 | не зупиняє запит, не замінює CSRF/authorization |
| rate limit login 10/30 с (IP), search 5/10 с (user) | перебір паролів, зловживання пошуком | `RateLimit_Login_…`, `Search_QuotaPerUser_…` | не замінює authorization |
| audit `incident.status_changed` + `incident_status_history` | невідстежувана привілейована зміна | REG-AUDIT-01 | лише triage; console sink |
| Production profile: без OpenAPI, без reset, connection string з env | розкриття документації, втрата даних | рис. 5–7, `ProductionProfileTests` | HTTPS/HSTS не налаштовано локально |

## 9. Dependency and secret audit

22.11.2026, SDK 10.0.401: `dotnet restore` — код 0, без NU1901–NU1904; `dotnet package list --vulnerable
--include-transitive` — код 0, для `SecureLab.Api` і `SecureLab.Api.Tests` джерело api.nuget.org не
повідомило про відомі advisories. Секрети: `git grep` по tracked files — реальних credentials немає;
`local-study-password` — accepted, study-only; тестові значення — не секрет (табл. 1, 6).

## 10. Residual risks

- login CSRF і account-creation CSRF (SC-06, SC-07) — потрібен анонімний bootstrap токена;
- HTTPS, redirect і HSTS не налаштовано й не перевірено на production host;
- `AllowedHosts: "*"` — для реального deployment потрібне окреме рішення;
- журнал лише в console sink, без захисту від зміни;
- `secure-app.html` і `session-state-demo.html` мають inline-скрипти, які блокує CSP, — сторінки поза основним flow.

## 11. Conclusion

Після виправлення F-01…F-03 і поширення antiforgery на весь cookie-периметр застосунок проходить повний
regression pack на reset-БД. Кінцевий стан зафіксовано тегом `v1.0.0`.

## 12. Evidence appendix

Рисунки звіту ЛР 6 (DEL-03), `docs/findings/*`, `docs/browser-security-evidence.md` (ЛР 5),
команди: `dotnet test SecureLab.sln -c Release`, curl-команди з рис. 10–17, 21–23, 34–35.
Значень паролів, cookie і токенів у доказах немає.
