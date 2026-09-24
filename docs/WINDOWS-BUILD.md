# بناء الطبقة الويندوزية (Windows Apps)

الطبقة الويندوزية (Host + Controller + Platform.Windows) تُبنى على **ويندوز فقط** لأنها تستخدم
WPF وDXGI وSendInput وMedia. لذلك هي في حلٍّ منفصل `RemoteDesktop.Windows.sln`، بينما يبقى
`RemoteDesktop.sln` متعدّد المنصّات (Core + Signaling + Tests) ويُبنى على Linux CI.

## المتطلّبات
- ويندوز 10 (1809+) أو ويندوز 11، معماريّة x64.
- .NET 8 SDK + حِمل عمل "‏.NET desktop development" (WPF).
- وصول للإنترنت لاستعادة حزم NuGet (SIPSorcery, Vortice, …).

## البناء
```powershell
dotnet build RemoteDesktop.Windows.sln -c Release
```
المخرجات:
- `RemoteDesktop.Host`   — تطبيق الخلفية على الجهاز المتحكَّم به (Tray).
- `RemoteDesktop.Controller` — تطبيق التحكم/العرض.
- `RemoteDesktop.Signaling` — خدمة الإشارات (تُشغَّل على خادم، راجع تعليمات النشر لاحقاً).

## التشغيل السريع (جهازان)
1. شغّل خدمة الإشارات (محلياً للتجربة): `dotnet run --project src/RemoteDesktop.Signaling`
   واضبط الأسرار (`Signaling__SessionTokenKeyBase64`، وبيانات TURN).
2. على **الجهاز المتحكَّم به**: شغّل `RemoteDesktop.Host` → أيقونة Tray → "Pair new device…" لعرض
   Host ID ورمز الإقران.
3. على **الجهاز المتحكِّم**: شغّل `RemoteDesktop.Controller` → أدخل Host ID والرمز → "Pair".
   يظهر على الـ Host طلب تأكيد محلي — وافق عليه مرة واحدة.
4. بعد الإقران: على الـ Controller اضغط "Connect". يظهر على الـ Host **مؤشّر جلسة مرئي**.
5. لإنهاء الجلسة من الـ Controller: **Ctrl+Alt+9** أو زر Disconnect.

## بنية الطبقة الويندوزية

| المكوّن | الملف/المجلد | الوظيفة |
|--------|--------------|---------|
| حقن الإدخال | `Platform.Windows/Input/SendInputSink.cs` | SendInput (ماوس/مفاتيح) بإحداثيات virtual-desktop |
| مفتاح الفصل | `Platform.Windows/Input/GlobalDisconnectHotkey.cs` | RegisterHotKey لـ Ctrl+Alt+9 على نافذة message-only |
| الشاشات وDPI | `Platform.Windows/Capture/DisplayEnumerator.cs` | EnumDisplayMonitors + GetDpiForMonitor |
| الالتقاط | `Platform.Windows/Capture/DesktopDuplicationCapturer.cs` | DXGI Desktop Duplication (Vortice) |
| WebRTC | `Platform.Windows/Rtc/WebRtcPeer.cs` | غلاف SIPSorcery: فيديو + data channel + ICE |
| الترميز | `Platform.Windows/Rtc/ScreenVideoSource.cs` | التقاط → VP8، مع pacing وتكيّف الجودة |
| فك الترميز | `Platform.Windows/Rtc/VideoDecodeBridge.cs` | إطارات مستقبَلة → BGRA للعرض |
| الإشارات | `Platform.Windows/Signaling/SignalingClient.cs` | مصادقة REST + WSS relay لإشارات موقّعة |
| تنسيق Host | `Platform.Windows/Rtc/HostConnectionOrchestrator.cs` | قبول عرض من جهاز موثوق فقط + بث + تنفيذ إدخال |
| تنسيق Controller | `Platform.Windows/Rtc/ControllerConnectionOrchestrator.cs` | إنشاء العرض + استقبال الفيديو + إرسال الإدخال |
| الإقران | `Platform.Windows/Pairing/*Coordinator.cs` | نقل الإقران عبر الإشارات مع تأكيد محلي |
| تطبيق Host | `RemoteDesktop.Host/` | Tray + مؤشّر إلزامي + نوافذ الإقران/الأجهزة الموثوقة |
| تطبيق Controller | `RemoteDesktop.Controller/` | العارض + التقاط الإدخال + Ctrl+Alt+9 |

## قيود وملاحظات معروفة
- **الترميز**: النسخة الأولية تستخدم **VP8** عبر `SIPSorceryMedia.Encoders` (مُدار، لتفادي بناء FFmpeg
  الأصلي). الانتقال إلى **H.264 بتسريع عتادي** (Media Foundation/NVENC) مخطّط لاحقاً — راجع `docs/LIBRARIES.md`.
- **الشاشات المحمية**: أثناء ظهور UAC/شاشة الدخول (secure desktop) يفقد Desktop Duplication الوصول ولا
  تُلتقط إطارات — سلوك موثّق ومقصود، لا نتجاوزه.
- **قياس زمن الاستجابة**: عرض الحالة موجود؛ قياس RTT الدقيق عبر إحصاءات WebRTC بند مفتوح للمرحلة 5.
- **بعض أسماء أعضاء SIPSorcery** قد تختلف بين الإصدارات الفرعية؛ تحقّق منها عند البناء على الإصدار المثبّت.
- **الإقران** يمرّ عبر خدمة الإشارات (نقل)، والثقة تُمنح فقط بالتأكيد المحلي على الـ Host.
