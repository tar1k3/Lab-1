# F-03. Пошук інцидентів доступний анонімно й без перевірки власника

**Статус:** Fixed
**Актив:** назви, severity і статуси інцидентів інших користувачів
**Компонент:** `GET /api/incidents/search` у `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`
**Пріоритет:** P1

## Передумови і межі огляду

Review-start commit `21694a0`; Development, локальна БД після reset; seed-інциденти alice (…0001)
і bob (…0002). Поза межами: інші read-маршрути (вони перевірені тестами ЛР 4).

## Відтворення

1. Без входу: `curl -s "http://localhost:5080/api/incidents/search?q="`.
   Очікувано: 401.
   Фактично: 200 і список, що містить інциденти всіх користувачів, зокрема alice і bob.
2. Увійти як alice і повторити пошук.
   Очікувано: лише власні інциденти alice.
   Фактично: у результаті є інцидент bob …0002.

## Вплив і ймовірність

**Статус перевірки:** оцінено за кодом і спостережено локально (запит без cookie повертав 200 зі списком).

Розкриття даних інших користувачів (BOLA): назви й статуси чужих інцидентів — impact High.
Маршрут не вимагав ні автентифікації, ні ролі, достатньо одного GET — likelihood High → P1.

## Першопричина

Маршрут пошуку з ЛР 2 був зареєстрований без `.RequireAuthorization()`, а запит до БД не мав
фільтра за власником, на відміну від `GET /api/incidents`, виправленого в ЛР 4.

## Виправлення

`.RequireAuthorization()` і фільтр `isAnalyst || OwnerUserId == currentUserId` з перевіреного principal,
як у `IncidentQueries.GetListAsync`; додатково ліміт `search` 5/10 с. Fixed commit `80c27e2`.

## Retest

Крок 1 на `80c27e2` → 401; крок 2 → лише інциденти alice, інциденту bob немає.

## Positive regression

alice знаходить власні інциденти; Analyst morgan бачить усі; `S02_SqlPayload_IsTreatedAsText_…`
підтверджує, що параметризація й allowlist сортування не змінились.

## Залишковий ризик

Інші майбутні маршрути читання потребують тієї самої перевірки; її не винесено в спільний політик-шар.

## Доказ

Тести `SearchMechanicsTests.Search_Anonymous_Returns401` і
`SearchRateLimitTests.Search_QuotaPerUser_…` (перевірка «лише свої інциденти»),
команда `dotnet test SecureLab.sln -c Release`, результат Passed.
