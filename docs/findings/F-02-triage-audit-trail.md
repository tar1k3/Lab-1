# F-02. Дозволений triage не залишає audit trail

**Статус:** Fixed
**Актив:** відстежуваність зміни `Incident.Status` (хто, коли і для якого інциденту змінив статус)
**Компонент:** `POST /api/incidents/{id}/triage` у `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`
**Пріоритет:** P2

## Передумови і межі огляду

Review-start commit `21694a0`; Development, локальна БД після
`dotnet run --project src/SecureLab.Api -- --reset-database`; навчальні акаунти alice (Reporter)
і morgan (Analyst); console sink журналу і таблиця `incident_status_history`.
Поза межами: інші переходи статусу, зовнішнє сховище журналів, ротація логів.

## Відтворення

1. alice створює новий інцидент (статус New).
2. alice: `POST /api/incidents/{id}/triage` → 403; у журналі є `Audit authorization.denied`.
3. morgan: `POST /api/incidents/{id}/triage` → 200, статус Triaged.
   Очікувано: запис про дозволену зміну в журналі аудиту й історії статусів.
   Фактично: ні журнал, ні `incident_status_history` не містять жодного запису про цей перехід.

## Вплив і ймовірність

**Статус перевірки:** спостережено локально (журнал консолі й таблиця історії до виправлення).

Дані не розкриваються і несанкціонованої дії немає, але без запису неможливо встановити, хто
й коли змінив статус, а журнал фіксував лише відмови — impact Medium. Перехід — штатна дія
кожного Analyst, тобто відбувається постійно — likelihood High → P2.

## Першопричина

Handler triage логував лише гілку відмови (`authorization.denied`); у гілці успіху змінювалися
`Status` і `UpdatedAtUtc` без запису в `IncidentStatusHistory` і без audit-події.

## Виправлення

У тій самій транзакції `SaveChanges` додається запис `IncidentStatusHistory`
(`ChangedByUserId`, `IncidentId`, `OldStatus`, `NewStatus`, `Note = "incident.triage: allowed"`,
`CreatedAtUtc`), після збереження — подія `Audit incident.status_changed` із тими самими полями й trace.
Тіла запиту, cookie й токенів у записах немає; користувач — за id. Fixed commit `024205e`.

## Retest

Кроки 1–3 на `024205e`: після відмови alice записів в історії немає; після дозволу morgan —
рівно один запис з усіма полями і подія `incident.status_changed` у журналі (звіт, рис. 36–39).

## Positive regression

Повторний triage того самого інциденту → 409, нового запису немає; відмова Reporter → 403,
статус New (`AuthorizationTests.A06`, `A07`).

## Залишковий ризик

Audit trail охоплює лише triage; інші майбутні переходи статусу треба підключити так само.
Журнал зберігається лише в console sink без захисту від зміни.

## Доказ

Тест `AuditTrailTests.RegAudit01_DeniedTriage_NoRecord_AllowedTriage_ExactlyOneRecord` (REG-AUDIT-01),
команда `dotnet test SecureLab.sln -c Release`, результат Passed; фактичні записи журналу — рис. 39.
