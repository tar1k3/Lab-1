# Докази безпеки браузера (DEL-01)

Рівень: добрий («Добре»). Паролів, значень cookie/token і export browser storage тут немає.

## Стенд і Git-стан

- ОС: Windows 11; browser: Opera GX (Chromium 155). API: `http://localhost:5080`; probes: `http://localhost:4173` (дозволений), `http://localhost:4174` (недозволений).
- Receipt `.scaffolds/lab-05.json`: release `lab-05-start-v1`, inherits `v0.4.0`, base_commit = entry_parent = `c74f7236d1d843c099e5b919e78f17c10e869d81`.
- Гілка `lab/5-browser-xss`:

| Commit | Зміст |
|---|---|
| `9bcf14a` | **vulnerable**: scaffold lab-05-start-v1 |
| `6f3bf6c` | seed публічного коментаря …0005 з marker |
| `86240df` | **fixed sink**: `setText` для опису й коментарів |
| `4755045` | Development-only CORS policy `Lab05BrowserProbe` для 4173 |
| `4d5f330` | `AllowCredentials()` для точного origin 4173 |
| `827e038` | enforcement CSP у спільній middleware |
| фінальний | ці документи; на нього вказує `v0.5.0`, main — fast-forward |

- Передавання: приватний remote `github.com/tar1k3/Lab-1` (гілка, `main`, тег `v0.5.0`).
- Сеанс (Application → Cookies → `http://localhost:5080`): `.AspNetCore.Identity.Application`, Domain `localhost`, Path `/`, HttpOnly ✓, SameSite Lax, Secure — ні (локальний HTTP, Development). Value не фіксується.

## Гіпотеза й REG-XSS-01

Карта — `docs/source-to-sink.md`. Гіпотеза: `textarea description` → `POST /api/incidents` JSON `description` → `Incident.Description` (PostgreSQL) → `GET /api/incidents/{id}` JSON `description` → `browser-lab.js`, `load`, рядок 10, `innerHTML` (HTML-контекст).

Процедура: `docker compose -f infra/compose.yaml up -d`; `SeedUsers__Password` задано поза Git; `dotnet run --project src/SecureLab.Api -- --reset-database`; `dotnet run --project src/SecureLab.Api`. Вхід alice на `/auth-lab.html` → `/browser-lab.html` → у Console перевірка `document.querySelector("#lab05-marker-target").getAttribute("data-lab05-marker")` → створити інцидент з marker з `lab05-marker.txt` → повторити перевірку. Потім morgan і bob у приватному вікні з тим самим Guid; потім seed-інцидент …0001 (коментар …0005).

| Спостереження | До fix (`9bcf14a`/`6f3bf6c`) | Після fix (`86240df`) | Доказ |
|---|---|---|---|
| Значення в БД/API | `description` = marker без змін | те саме, символ у символ | рис. 23, 32 |
| DOM Description | у `#description` елемент `<img src=x onerror=…>` | один текстовий вузол | рис. 20, 31 |
| Дія marker | `null` → `'executed'` | `null` → `null`, запиту на `x` немає | рис. 19, 30 |
| Апостроф/лапки, `5 < 7`, Unicode, HTML-like | `<b>` став елементом, `<script>` обрізав опис | усе відображено буквально | рис. 12–17, 33 |
| Довжина (≤ 4000, ЛР 2) | 3900 символів відображено | `textContent.length` = 3900 | рис. 15, 34–35 |
| Morgan / Bob | Morgan: GET 200, `'executed'`; Bob: GET 404, `null` | — | рис. 21, 22 |

Finding: **актив** — сеанс користувача (зокрема Analyst Morgan) і дані від його імені; **передумови** — обліковий запис Reporter і право створити інцидент; **першопричина** — `browser-lab.js`, `load`, рядки 10 і 14 записують збережений текст користувача в `innerHTML`; **safe DOM diff** — `git diff 9bcf14a 86240df -- src/SecureLab.Api/Client/browser-lab.js`; **retest** — той самий marker відображено як текст, перевірка `null`; **залишковий ризик** — інші сторінки/поля поза flow, контексти атрибутів і URL, сторонні скрипти.

Негативний контроль: на `9bcf14a` той самий marker створює `<img>` у `#description` і `#comments`, атрибут стає `'executed'` для alice і morgan; bob отримує 404.

## CORS і REG-CORS-01

Probes: `py -3 -m http.server 4173 --bind 127.0.0.1 --directory demonstrations/cors-probe` (і 4174). Policy — лише Development, один exact origin 4173.

| Сценарій | Request доходить до API? | JavaScript читає response? | Чому |
|---|---|---|---|
| same-origin 5080 → API | так | так | той самий origin |
| simple GET 4173 → API | так | ні без коректної CORS-відповіді | SOP + CORS у браузері |
| simple GET 4174 → API | так | ні, origin не в allowlist | SOP + CORS |
| curl → API | так | не застосовується | не браузер |
| preflighted 4173 → API | спочатку OPTIONS; actual GET — лише після дозволу | ні без успішного preflight | browser preflight |

| Клієнт | До policy | Fixed state | Доказ |
|---|---|---|---|
| Same-origin 5080 | 200 | 200 (з входом) | рис. 6 |
| Allowed 4173 | 200 на сервері, `Failed to fetch`; preflight — GET не надіслано | simple 200 прочитано, ACAO 4173; OPTIONS 204 → GET 200 | рис. 37–38, 41–42 |
| Disallowed 4174 | як 4173 | simple 200 на сервері, ACAO немає, блок; OPTIONS 204 без `Access-Control-Allow-*` | рис. 43–44 |
| curl | 200 | 200 для будь-якого Origin; ACAO лише для 4173 | рис. 50, 58 |

Порядок: `UseRouting` → `UseCors` → `UseAuthentication` → `UseAuthorization`; запит з 4173 без cookie до `/api/incidents` → скрипт прочитав `401` (рис. 40). curl читає відповідь, бо SOP — правило браузера; автентифікацію й авторизацію API він проходить так само.

## Добре

- **IncidentComment.Text**: seed …0005 (Morgan, публічний) до інциденту Alice …0001; до fix `'executed'` і `<img>` у `#comments` (рис. 25–26), після — текст і `null` (рис. 28–29). Єдиний helper `setText` (`browser-lab.js`, рядки 4–6), виклики — рядки 13 і 17.
- **Preflight**: 4173 — OPTIONS 204 з `Access-Control-Allow-Origin/-Methods/-Headers`, далі GET 200; 4174 — OPTIONS 204 без дозвільних заголовків, actual GET не надіслано.
- **Cookie-дослід**: 4173 з cookie Alice → 200 зі списком, `Access-Control-Allow-Credentials: true` (рис. 47); 4174 → запит виконано (200), відповідь не віддано (рис. 46); без `AllowCredentials()` → помилка «header … is '' which must be 'true'» (рис. 48); без входу → 401 прочитано (рис. 49). Кінцевий `AllowCredentials()` — лише в Development policy для 4173.

| Директива CSP | Фактичні ресурси | Джерело / причина |
|---|---|---|
| default-src | запасне правило | `'self'` |
| script-src | `app.js`, `auth-lab.js`, `browser-lab.js` | `'self'`; inline-скриптів у flow немає |
| style-src | `styles.css` | `'self'`; inline-стилів у flow немає |
| connect-src | `fetch` на той самий origin 5080 | `'self'` |
| img-src | зображень у flow немає | `'self'`; кероване порушення `new Image()` до 4173 заблоковано (рис. 55) |
| frame-ancestors | вбудовування не потрібне | `'none'`, явно, бо не успадковує `default-src` |

Місце зміни: наявна `app.Use` у `Program.cs` (рядки 74–91), виняток лише для `/scalar` у Development. Документ `/` — 200, один заголовок CSP (рис. 53, 57); flow `browser-lab.html` без порушень (рис. 54).

| Header | Значення | Ризик і межа |
|---|---|---|
| X-Content-Type-Options | `nosniff` (baseline) | забороняє MIME sniffing; не замінює правильний `Content-Type` і валідацію |
| Referrer-Policy | `no-referrer` (baseline) | URL із Guid не передається іншим сайтам; клієнт не має зовнішніх переходів, тож policy підходить; не дозволяє класти секрети в URL |
| frame-ancestors | `'none'` | захист від clickjacking; не замінює авторизацію і виправлення XSS |

Review XSS/CORS/CSRF/authorization — у звіті DEL-03.
