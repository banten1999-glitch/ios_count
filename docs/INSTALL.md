# التثبيت وإلغاء الصلاحيات والإزالة الكاملة

## 1. النشر (البناء)
على ويندوز مع .NET 8 SDK:
```powershell
# الجهاز المتحكَّم به (Host)
dotnet publish src/RemoteDesktop.Host -c Release -r win-x64 --self-contained false -o publish\host

# الجهاز المتحكِّم (Controller)
dotnet publish src/RemoteDesktop.Controller -c Release -r win-x64 --self-contained false -o publish\controller
```

## 2. التثبيت (لكل مستخدم، بلا صلاحيات مدير)
```powershell
# على الجهاز المتحكَّم به:
install\Install-Host.ps1 -PublishDir publish\host
#   ينسخ إلى %LOCALAPPDATA%\RemoteDesktopControl\app
#   ينشئ مهمة تشغيل عند تسجيل الدخول (asInvoker، بلا رفع صلاحيات)
#   يشغّل التطبيق (أيقونة Tray)

# على الجهاز المتحكِّم:
install\Install-Controller.ps1 -PublishDir publish\controller
```
> ملاحظة تصميمية: الـ Host يعمل داخل جلسة المستخدم (لا كخدمة Session 0) لأن التقاط الشاشة وحقن
> الإدخال يتطلّبان جلسة تفاعلية. لذا التشغيل التلقائي عبر "مهمة عند تسجيل الدخول" بموافقتك الصريحة.
> التطبيق **غير مخفي**: أيقونة Tray دائمة، ومؤشّر جلسة مرئي أثناء أي اتصال.

## 3. الإقران (مرة واحدة)
1. على الـ Host: Tray → **Pair new device…** → يظهر Host ID ورمز الإقران.
2. على الـ Controller: أدخل Host ID والرمز → **Pair**.
3. على الـ Host يظهر طلب **تأكيد محلي** — وافق مرة واحدة. بعدها يصبح الوصول دون حضور متاحاً.

## 4. إلغاء صلاحية جهاز (Revoke)
- على الـ Host: Tray → **Trusted devices…** → اختر الجهاز → **Revoke selected**.
- الإلغاء فوري ومحلي؛ الجهاز الملغى يُرفض في المصادقة اللاحقة مباشرةً (معرفة المعرّف لا تكفي).

## 5. الإزالة الكاملة
```powershell
# الجهاز المتحكَّم به:
install\Uninstall-Host.ps1            # يزيل التطبيق والمهمة، ويُبقي الهوية/الثقة
install\Uninstall-Host.ps1 -PurgeData # يزيل كل شيء بما فيه مفتاح الهوية وقائمة الثقة

# الجهاز المتحكِّم:
install\Uninstall-Controller.ps1 [-PurgeData]
```
- `-PurgeData` يحذف مفتاح الجهاز (المحمي بـ DPAPI) وقوائم الثقة/الاستضافة من
  `%LOCALAPPDATA%\RemoteDesktopControl`.
- خدمة الإشارات/TURN تُزال من الخادم عبر: `docker compose -f deploy/docker-compose.yml down -v`.

## 6. خيار MSI (WiX) — لاحقاً
حزمة MSI عبر WiX Toolset ممكنة لتثبيت أنظف مع فك تثبيت من "التطبيقات والميزات". السكربتات أعلاه
توفّر تثبيتاً/إزالة شفّافين بلا صلاحيات مدير للنسخة الأولية؛ إضافة WiX بند مفتوح.

## ملاحظة حول مضادات الفيروسات
لتفادي حجب برنامج جديد غير معروف: **وقّع الملفات التنفيذية (code signing)** بشهادة موثوقة. لا نلجأ
لأي إخفاء أو تضليل — الحل هو التوقيع والشفافية.
