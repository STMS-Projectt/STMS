# مراجعة الواجهة وإمكانية الوصول

| الصفحة | التحقق الفعلي | ما لم يثبت |
|---|---|---|
| Sign in وBiometricVerify | رسائل كلمة المرور الخاطئة، الدخول بالأدوار، إعداد Passkey افتراضي | قارئ شاشة؛ جهاز فعلي؛ FaceLab وكاميرا حقيقية |
| Department dashboard وMy Requests | تنقل AJAX، البحث الصوتي بمحاكاة، عدم تجاوز عرض المستند عند 1440 و1024 و768 و390 و320 | التعرف على كلام حقيقي، إمكانية الوصول الكاملة |
| Create requisition وAdd item | النوافذ، حفظ المسودة والبندين، تحديث المحتوى بلا تحميل كامل للمسودة، إظهار Under Review | قياس أداء التحميل، كل أنماط الخطأ ورفع الملفات |
| بقية لوحات الأدوار | التحقق من وجهات الدخول | مراجعة بصرية تفصيلية وفحص العرض المتجاوب لكل صفحة |
| التقييم والتصويت والمطابقة والإدارة | بعض المسارات مثبتة بطلبات HTTP وSQL | Modal وAJAX ولوحة المفاتيح لكل عملية |

[نافذة إنشاء الطلب](phase4-browser-evidence/department-draft-modal.png) · [عرض سطح المكتب](phase4-browser-evidence/department-requests-1440.png) · [عرض الجوال](phase4-browser-evidence/department-requests-390.png) · [الطلب بعد الإرسال](phase4-browser-evidence/department-submitted.png).

وجود عناصر dialog وعناوينها وحالة التحقق لا يثبت WCAG 2.2 AA. التباين المقاس، قارئ الشاشة، تكبير 200% والحركة المخفضة: NOT EXECUTED. دليل الحالات المنفذة في [نتائج المتصفح](phase4-browser-evidence/results.json).
