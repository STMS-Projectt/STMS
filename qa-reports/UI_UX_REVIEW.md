# تدقيق تجربة الاستخدام وإمكانية الوصول

مراجعة HTML/Razor/CSS/JS ثابتة مع اختبار HTTP للمحتوى الجزئي. لا معاينة مرئية أو قياس contrast أو اختبار قارئ شاشة منفذ.

| المجموعة | الصفحات والملاحظات |
|---|---|
| عامة | Home/Index وPrivacy وAccount/AccessDenied وShared/Error: روابط الدخول موجودة؛ About/Contact والمؤشرات العامة أقل من V5 (B21). |
| الدخول والأمان | Account/Login وBiometricSetup: خيارات وجه وهاتف وكلمة مرور؛ شرح المتصفح/الهاتف مفيد، لكن تسمية «البصمة» لا تضمن نوع التحقق؛ Liveness مفقود B01. |
| الإدارة | AdminDashboard، AdminOperations/Register,Request,Tender، Employees وDepartments وSuppliers: صفحات مستقلة وتأكيدات؛ لوحات ومسارات متفاوتة، لا رؤى فترة/تصدير B13. |
| القسم | PurchaseRequisitions/Dashboard,Index,Create,Edit,Details,Committee وRequisitionItems: نموذج وتاريخ وحالات؛ المبرر لا يحل محل وصف مستقل؛ البحث الصوتي يعاد تحميل DOM دون إعادة الربط B16. |
| التوريد | SourcingDashboard، RequisitionReviews، Tenders وInvitations: حالة واضحة عادة؛ النصوص تشير أحياناً إلى الجولة الثانية حصراً؛ لا جدول تواريخ للجولات أو تفسير واضح لتعذر الاستثناء B04/B07. |
| المورد والتسجيل | SupplierRegistration وSupplierWorkspace وBids وBidItems وBidDocuments: ملكية على المسارات، تحميل الملفات، فتح البنود؛ النتيجة النهائية قد تظهر في إشعار بينما شارة العرض لا تتغير B19. |
| المطابقة والتقييم | Compliance/Pending,Review,MarkNonCompliant وCommitteeReviews/Index,Assignments,Details,Bid,AiReport: جداول مقارنة مفيدة؛ لا Pending Clarification، ولا خطوة «إرسال التقييم» مستقلة. |
| المدير واللجنة العليا | EvaluationWorkflow/Index,Details: عرض الدرجات وملف التقرير وسجل التصويت؛ لا ينبه المستخدم إلى إلزام شرح الرفض لأن الخادم لا يفرضه. اعرض تقدم النصاب والمرحلة بصورة ثابتة حتى بعد تصويت المستخدم. |
| التواصل والحساب | Clarifications/Create,Index,Details وNotifications/Index وProfile/Index: تتبع محادثة وتنبيهات وصورة؛ القوائم المتزايدة تحتاج صفحات، وشاشة «لا مستلمين» تحتاج شرح سياق المرحلة. |

مشاهدات مشتركة: _Layout يبدّل shell حسب الدور ويدعم روابط مباشرة وAJAX؛ workspace.js يستأنف استجابة الحوار ويدير التركيز ومؤشر التحميل؛ تحقق من Escape والرجوع وتغيّر الدور وانتهاء الجلسة أثناء POST وعدم فقد مدخلات invalid. DOM dialogs لها accessible names، وبعض أشكال scaffolding القديمة تستخدم نمطاً مختلفاً. افحص Tables عند 320px و768px و1440px، وإمكانية الوصول لإجراءات طرف الصف وتكبير 200% وحركة Loader المخفضة؛ هذه اختبارات غير منفذة. جرّب لوحة مفاتيح فقط، أخطاء النموذج، ملفات تالفة، وتحميل متأخر ورفض الميكروفون في المتصفح.
