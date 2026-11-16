# المرحلة الخامسة — التحقق الحيوي

FaceLab موجود في `FaceLab.cs` و`face-lab.js` ويعمل داخل حساب مسجّل. يتطلب `FacePlusPlus:EnableFreeLab=true` ومفتاحًا وسرًا. التفعيل الافتراضي مغلق، ولم تتوفر بيانات اعتماد آمنة في متغيرات العملية. بيانات المحادثة لم تُستخدم ولم تُكتب على القرص؛ دوّرها لدى Face++ واضبط البديل محليًا. استخدم فقط الحساب المجاني بلا فوترة. لا حاجة لأي تعديل على ملفات Git.

في PowerShell، شغّل التطبيق من جلسة محلية واحدة بعد ضبط القيم دون إظهارها أو تسجيلها في سجل الأوامر:

```powershell
$env:FacePlusPlus__ApiKey = Read-Host 'New Face++ key' -MaskInput
$env:FacePlusPlus__ApiSecret = Read-Host 'New Face++ secret' -MaskInput
$env:FacePlusPlus__EnableFreeLab = 'true'
dotnet run --project SourcingTenderSystem/SourcingTenderSystem/SourcingTenderSystem.csproj
```

ثم افتح Sign-in security عبر HTTPS أو localhost واستخدم شخصًا موافقًا. اختبر منح الإذن ورفضه، إطارًا بلا وجه ووجهًا واحدًا ووجوهًا متعددة، الاتجاهات، المطابقة والرفض، إعادة إرسال الرمز وانتهاءه، إيقاف الكاميرا وحذف المرجع. لا تستخدم صورًا شخصية غير مصرح بها.

**BLOCKED:** الاستدعاء الحقيقي لـ Detect/Compare والكاميرا وتسجيل المرجع الفعلي، لغياب إعداد مفاتيح مدوّرة آمنة وشخص موافق. المصادقة المستقلة عبر Face++ ما زالت مغلقة، وناقل JPEG القديم كذلك. **Passkey** تحقق في Chrome بمصدّق WebAuthn افتراضي؛ الجهاز الحيوي الفعلي **NOT EXECUTED**. دعم WebAuthn لا يضمن أن الجهاز استخدم الوجه بدل PIN.
