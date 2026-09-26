# نشر خدمتي الإشارات وTURN

كلا الجهازين يحتاجان الوصول إلى:
1. **خدمة الإشارات** (ASP.NET Core) عبر HTTPS/WSS — للمصادقة وتبادل SDP/ICE وإصدار بيانات TURN.
2. **خادم TURN/STUN** (coturn) — لعبور NAT عند تعذّر الاتصال المباشر.

يمكن تشغيلهما على **VPS صغير واحد** باسم DNS عام.

## 1. المتطلّبات
- خادم لينكس (1 vCPU / 1–2GB RAM يكفي للبداية) بعنوان IP عام واسم DNS، مثل `turn.example.com`.
- منفذان مفتوحان للويب (80/443) ومنافذ TURN (أدناه).
- Docker + Docker Compose.
- شهادة TLS: يصدرها Caddy تلقائياً للإشارات؛ ولـ `turns:` انسخ الشهادة إلى `deploy/certs/`.

## 2. الإعداد
```bash
cp deploy/.env.example deploy/.env
# حرّر deploy/.env:
#   PUBLIC_HOST=turn.example.com
#   SESSION_TOKEN_KEY_BASE64=$(openssl rand -base64 32)
#   TURN_SHARED_SECRET=$(openssl rand -hex 32)

# للـ turns: ضع الشهادة والمفتاح (يمكن نسخهما من مجلد Caddy بعد أول إصدار، أو أصدرهما يدوياً)
mkdir -p deploy/certs   # ضع fullchain.pem و privkey.pem هنا

# عدّل النطاق في deploy/caddy/Caddyfile إلى نطاقك.
docker compose -f deploy/docker-compose.yml up -d --build
```
تحقّق: `curl https://signaling.example.com/health` → `{"status":"ok"}`.

ثم في تطبيقَي Host/Controller اضبط `SignalingBaseUrl` إلى `https://signaling.example.com`.

## 3. المنافذ المطلوبة

| الخدمة | المنفذ | البروتوكول | الاتجاه | الغرض |
|--------|--------|------------|---------|-------|
| Reverse proxy | 80 | TCP | وارد | تحدّي ACME + تحويل إلى 443 |
| Reverse proxy | 443 | TCP | وارد | HTTPS/WSS للإشارات |
| STUN/TURN | 3478 | UDP + TCP | وارد | ICE/عبور NAT |
| TURN over TLS | 5349 | TCP (TLS) | وارد | TURN مشفّر (`turns:`) |
| TURN relay | 49160–49200 | UDP | وارد | نطاق تحويل الوسائط |

> يمكن توسيع نطاق الـ relay حسب عدد الجلسات المتزامنة. الإشارات لا تمرّ عبرها الوسائط (P2P/relay فقط عند الحاجة).

## 4. التكاليف المتكرّرة (تقديرية)

| البند | ملاحظة | تقدير شهري |
|-------|--------|------------|
| VPS صغير (إشارات + coturn) | 1 vCPU / 1–2GB | منخفض (يعتمد على المزوّد) |
| نطاق DNS | سنوي (يوزّع شهرياً) | زهيد |
| شهادة TLS | Let's Encrypt عبر Caddy | مجاني |
| حركة TURN relay | تُحتسب فقط عند فشل الاتصال المباشر ومرور الوسائط عبر الخادم؛ قد ترتفع مع الاستخدام | متغيّر |
| شهادة توقيع الكود (اختياري لكن مُوصى به لتفادي تحذيرات مضاد الفيروسات) | سنوية | متوسط |

> بما أن الجهازين يملكهما المستخدم نفسه وغالباً على شبكات منزلية، سينجح الاتصال المباشر (STUN) في كثير من الحالات
> ويقلّ اعتماد TURN — ما يخفّض تكلفة الحركة. TURN ضروري كخيار احتياطي خلف NAT صارم/CGNAT.

## 5. ملاحظات أمنية للنشر
- الأسرار (`SESSION_TOKEN_KEY_BASE64`، `TURN_SHARED_SECRET`) من `.env` فقط، ولا تُلتزم في المستودع (مستثناة في `.gitignore`).
- بيانات TURN التي تصدرها الخدمة **قصيرة الصلاحية** (HMAC) — لا بيانات دائمة.
- خدمة الإشارات خلف TLS حصراً؛ لا تعرّض المنفذ 8080 مباشرةً للإنترنت.
- فعّل جدار حماية يسمح فقط بالمنافذ أعلاه.
