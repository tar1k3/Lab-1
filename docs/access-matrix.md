# Матриця доступу SecureLab (baseline 2-A)

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник ресурсу має значення? | Стан реалізації |
|---|---|---|---|---|---|---|---|
| Перегляд списку | добрий | deny, 401 | own | all | own | Так для Reporter і Administrator | Реалізувати й перевірити |
| Пошук GET /api/incidents/search | відмінний (A-11) | не виконувалось: рівень «Відмінно» | не виконувалось | не виконувалось | не виконувалось | не виконувалось | не виконувалось |
| Зведення GET /api/incidents/severity-summary | відмінний (A-11) | не виконувалось: рівень «Відмінно» | не виконувалось | не виконувалось | не виконувалось | не виконувалось | не виконувалось |
| Перегляд деталей | достатній: entry-контракт ЛР 5; головна action | deny, 401 | owner — allow; non-owner — deny, 404 | allow для аналізу інциденту | owner — allow; non-owner — deny, 404 | Так | Реалізувати й перевірити |
| Створення інциденту | успадкований baseline | deny, 401 | allow for self | allow for self | allow for self | Сервер призначає власника | Перевірити baseline |
| Update content | добрий | deny, 401 | owner — allow (title, description, occurredAtUtc); non-owner — deny, 404 | deny, 403 | owner — як Reporter; non-owner — deny, 404 | Так | Реалізувати й перевірити |
| Delete | добрий | deny, 401 | owner — allow, якщо статус New, інакше deny, 409; non-owner — deny, 404 | deny, 403 | owner — як Reporter; non-owner — deny, 404 | Так | Реалізувати й перевірити |
| Зміна severity/status (POST /api/incidents/{id}/triage) | добрий | deny, 401 | deny, 403 | allow лише New → Triaged; інший статус — deny, 409 | deny, 403 | Ні | Реалізувати й перевірити |
| Перегляд internal comment | добрий | deny, 401 | deny, 404 (у details не показується навіть власнику) | allow після авторизації для parent incident | deny, 404 | Ні для internal rule; доступ до parent incident перевіряється окремо | Реалізувати й перевірити |
| Export/audit | відмінний, якщо flow існує | не застосовується | не застосовується | не застосовується | не застосовується | не застосовується | не застосовується |

Child resource: `IncidentComment` (`GET /api/incident-comments/{id}`). Через коментар можна непрямо отримати дані інциденту, тому доступ до коментаря надається лише тому, хто має право читати parent incident; для non-owner — 404.

Політика відмов: чужий ресурс — 404, ідентичний відповіді для неіснуючого (приховуємо факт існування); 403 — ресурс видимий, але дія заборонена або немає ролі; 409 — недопустимий поточний статус.
