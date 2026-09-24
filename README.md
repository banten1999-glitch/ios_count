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
| 2 | الإقران والتخزين الآمن (DPAPI) + قائمة الأجهزة الموثوقة | ✅ |
| 3 | منطق الإدخال + تحرير المفاتيح المعلّقة + سلامة الإشارات (منطق Core؛ ربط WebRTC/SendInput لاحقاً) | ✅ |
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

## ما أُنجز في المرحلة 2 (الإقران والتخزين الآمن)

**`RemoteDesktop.Core`**:
- **الإقران**: `PairingCode` (رمز قصير عالي الإنتروبيا، صلاحية قصيرة، مقارنة constant-time)، `PairingProof` (إثبات امتلاك المفتاح مربوط بالرمز ومعرّف الـ Host)، و`HostPairingService` — آلة حالة الإقران: `Begin → Submit → Confirm`، مع حدّ محاولات (T14) ورفض التحديات المنتهية، **ولا تُمنح الثقة إلا بعد تأكيد صريح** (T12).
- **التخزين الآمن**: `ISecretStore` مع `DpapiSecretStore` (Windows، يشفّر مفتاح الجهاز الخاص بربطه بحساب المستخدم) و`InMemorySecretStore` (اختبار/تطوير فقط)، و`DeviceIdentityProvider` (توليد/تحميل الهوية من المخزن الآمن).
- **الأجهزة الموثوقة**: `ITrustedDeviceStore` مع `JsonFileTrustedDeviceStore` (كتابة ذرّية) و`TrustManager` — الوصول مسموح فقط لجهاز موثوق **وغير مُلغى** (T15).

## ما أُنجز في المرحلة 3 (الإدخال وسلامة الإشارات)

**`RemoteDesktop.Core`**:
- **أحداث الإدخال**: `InputEvent` متعدّد الأشكال (ماوس/عجلة/مفاتيح) بإحداثيات مُطبّعة [0,1] لدعم تعدد الشاشات وDPI، و`InputEventCodec` (JSON آمن لا يرمي استثناءً على إدخال فاسد).
- **التنفيذ والأمان**: `IInputSink` (تنفيذ عبر `SendInput` لاحقاً)، و`RemoteInputExecutor` مع ترقيم تسلسلي (إسقاط الأحداث القديمة/المكرّرة)، و`PressedInputTracker` — **تحرير كل المفاتيح/الأزرار المعلّقة عند أي انقطاع** (best-effort لا يتوقّف عند خطأ).
- **الجلسة**: `HostControlSession` (تُحرِّر الإدخال عند `Stop` وتتجاهل الرسائل بعده، idempotent)، و`ReconnectPolicy` (محاولات محدودة + backoff + jitter) و`SessionState` واضحة.
- **سلامة الإشارات (ضد MITM — T3/T6)**: `SignedSignal` — توقيع مغلّف الإشارة (بما فيه SDP وبصمة DTLS) بمفتاح الهوية والتحقق منه ضد **المفتاح المقترن** بمعزل عن خادم الإشارات، مع `SdpFingerprint` لاستخراج/مطابقة البصمة.

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
