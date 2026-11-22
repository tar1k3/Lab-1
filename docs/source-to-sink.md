# Власна карта source-to-sink

Рівень: «Добре». Стан до fix: `9bcf14a` (scaffold lab-05-start-v1). Fixed: `86240df`. Локальний користувач для створення: alice (Reporter); глядач: morgan (Analyst).

## Потік 1. Incident.Description

Запис: інцидент «XSS marker1», Guid `4a36d7d7-4266-48af-b291-…`, створений Alice через `/browser-lab.html`.

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | `Client/browser-lab.html`, рядок 5, `<textarea name="description">` | канонічний marker з `lab05-marker.txt` | вводить користувач — недовірене |
| Request | `Client/browser-lab.js`, обробник submit форми `#edit`, `JSON.stringify({ title, description, … })` | JSON `description` у `POST /api/incidents` | Network → Payload; контролює клієнт |
| Validation/mapping | `Scaffolding/Lab02Endpoints.cs`, `POST /api/incidents`, `CreateIncidentRequest.Description` | обов'язковий, ≤ 4000 символів, `Trim()` | перевірка формату, не HTML-очищення; значення не стає trusted |
| Storage | `Data/Entities/Incident.cs`, `Incident.Description` → колонка `description` у PostgreSQL | marker без змін | збережене значення користувача |
| Response | `Application/Incidents/IncidentQueries.cs`, `GetDetailsAsync` → `Presentation/Contracts/IncidentResponses.cs`, `IncidentDetailsResponse` | JSON `description` у `GET /api/incidents/{id}` | Network → Response: marker символ у символ (рис. 23 і 32); JSON escape не захищає HTML sink |
| Client transform | `Client/browser-lab.js`, функція `load`, `current = await response.json()` | `current.description` | без перетворень |
| Sink/context | **До:** `browser-lab.js`, `load`, рядок 10, `#description.innerHTML = current.description` — HTML-контекст, небезпечно. **Після:** рядок 13, `setText(#description, current.description)` → `textContent` — текстовий вузол, безпечно | `#description` | рис. 20 (`<img>` у DOM, `executed`) → рис. 31 (текстовий вузол, `null`) |

## Потік 2. IncidentComment.Text

Запис: публічний seed-коментар `30000000-0000-0000-0000-000000000005` (автор Morgan, `IsInternal = false`) до інциденту Alice `20000000-0000-0000-0000-000000000001`.

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | `Data/DbSeeder.cs`, `SeedAsync`, константа `Lab05Marker` | marker | у продукті текст вводить будь-який автор коментаря — недовірене |
| Request | — (endpoint створення коментаря немає; seed) | — | — |
| Validation/mapping | `DbSeeder.SeedAsync`, `IncidentComments.AddRange(…)` | `IncidentComment.Text = Lab05Marker` | без очищення |
| Storage | `Data/Entities/IncidentComment.cs`, `IncidentComment.Text` | marker | збережене значення |
| Response | `IncidentQueries.GetDetailsAsync` → `IncidentCommentResponse` | JSON `comments[].text` | Network → Response |
| Client transform | `browser-lab.js`, `load`, цикл `for (const comment of current.comments ?? [])` | `comment.text` | `replaceChildren()` очищує контейнер, дублювання немає |
| Sink/context | **До:** рядок 14, `paragraph.innerHTML = comment.text` — HTML-контекст. **Після:** рядок 17, `setText(paragraph, comment.text)`; `p` створює код через `createElement` | абзац у `#comments` | рис. 25–26 (`executed`, `<img>`) → рис. 28–29 (`null`, текстовий вузол) |

## Інвентаризація sink у `browser-lab.js`

| Файл/функція | Дані | Контекст | Рішення |
|---|---|---|---|
| `load`, `status.textContent` | HTTP-статус, server-generated | текст | безпечно |
| `load`, `#title.textContent` | `Incident.Title`, користувацьке | текст | безпечно |
| `load`, `#description` | `Incident.Description`, користувацьке | було HTML (`innerHTML`) → текст | виправлено `setText` |
| `load`, абзац у `#comments` | `IncidentComment.Text`, користувацьке | було HTML (`innerHTML`) → текст | виправлено `setText` |
| `load`, `#edit.elements.description.value` | `Incident.Description` | значення `textarea` (властивість, не HTML) | безпечно |
