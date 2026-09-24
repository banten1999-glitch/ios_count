# Remote Desktop Control (تحكم عن بُعد شفّاف قائم على الموافقة)

أداة تحكم عن بُعد بين جهازي ويندوز يملكهما المستخدم نفسه، مع **وصول دون حضور بعد إقران صريح لمرة واحدة**،
على غرار الوضع غير المراقَب في TeamViewer / AnyDesk. التصميم شفّاف: **مؤشّر جلسة مرئي إلزامي** وتشفير قياسي فقط.

> ⚠️ **نسخة مبدئية قيد التطوير — ليست جاهزة للإنتاج قبل مراجعة أمنية مستقلة.**

## الوثائق
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — المعمارية والمكوّنات والتدفّقات.
- [`docs/THREAT-MODEL.md`](docs/THREAT-MODEL.md) — تحليل التهديدات (STRIDE) والمخاطر المتبقية.
- [`docs/LIBRARIES.md`](docs/LIBRARIES.md) — المكتبات وتراخيصها وأسباب الاختيار.
- [`docs/IMPLEMENTATION-PLAN.md`](docs/IMPLEMENTATION-PLAN.md) — الخطة على 8 مراحل وسيناريوهات الاختبار.

## حالة التنفيذ

| المرحلة | الوصف | الحالة |
|--------|-------|--------|
| 0 | هيكل الحل + المكتبة المشتركة (`RemoteDesktop.Core`) | ✅ |
| 1 | خدمة الإشارات المصادَقة (challenge/response + anti-replay + TURN creds + rate limiting) | ✅ |
| 2 | الإقران والتخزين الآمن (DPAPI) | ⏳ |
| 3 | قناة WebRTC + الإدخال | ⏳ |
| 4 | التقاط الشاشة + الفيديو + مؤشّر الجلسة | ⏳ |
| 5–7 | الأداء، الخدمة/التثبيت، التوثيق والمراجعة | ⏳ |

## ما أُنجز في المرحلتين 0 و1

**`RemoteDesktop.Core`** (cross-platform):
- هوية الجهاز بمفاتيح **ECDSA P-256** (`DeviceIdentity` / `DevicePublicKey`)؛ معرّف الجهاز مشتقّ من المفتاح العام (معرفة المعرّف وحدها لا تكفي للاتصال).
- تحدّي مصادقة single-use بترميز canonical آمن (`AuthChallenge`) والتحقق منه (`ChallengeVerifier`).
- عقود البروتوكول المشتركة (رسائل الإشارات، TURN، الأجهزة الموثوقة).

**`RemoteDesktop.Signaling`** (ASP.NET Core):
- `POST /api/devices/register` — تسجيل المفتاح العام (لا يمنح وصولاً بذاته).
- `POST /api/auth/begin` — إصدار تحدٍّ لجهاز معروف.
- `POST /api/auth/complete` — التحقق من التوقيع + منع إعادة الإرسال، ثم إصدار **session token** قصير + **بيانات TURN** (نظام coturn REST/HMAC).
- **Rate limiting** لكل IP على مسارات المصادقة (T9/T14).

## البناء والاختبار

يتطلّب **.NET 8 SDK**.

```bash
dotnet build RemoteDesktop.sln -c Debug
dotnet test  RemoteDesktop.sln -c Debug
```

تشغيل خدمة الإشارات محلياً:

```bash
cd src/RemoteDesktop.Signaling
dotnet run
# ثم: curl http://localhost:5xxx/health
```

### الإعداد المطلوب (أسرار لا تُلتزم في المستودع)
- `Signaling:SessionTokenKeyBase64` — مفتاح HMAC ≥ 32 بايت (base64) لتوقيع رموز الجلسة. **إلزامي.**
- `Signaling:Turn:SharedSecret` + `Signaling:Turn:Uris` — لإصدار بيانات TURN.

تُمرَّر عبر user-secrets أو متغيّرات البيئة، مثل:
```bash
export Signaling__SessionTokenKeyBase64="$(openssl rand -base64 32)"
```

## حدود صريحة (Non-Goals)
لا إخفاء/جلسات خفية، لا تعطيل مؤشر الاتصال، لا تجاوز مضادات فيروسات أو أنظمة امتحانات، لا حقن شيفرة/rootkits،
لا تثبيت سري أو persistence دون موافقة، لا تجاوز UAC، لا منافذ بلا مصادقة. التفاصيل في `docs/ARCHITECTURE.md §8`.
