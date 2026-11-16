# تدقيق سير العمل

المعيار الأعلى: توضيح المستخدم الأخير؛ ثلاث جولات إجمالاً، وحد أدنى ثلاثة موردين متميزين في الجولتين الأولى والثانية، ثم عرض صالح واحد أو أكثر بعد الثالثة؛ الصفر ممنوع. عضوية مدير التوريد في التقييم وموافقته الإدارية واجبتان ومستقلتان.

| الكيان | من ← إلى | الإجراء والدور | الحفظ والإشعار | الحكم والدليل |
|---|---|---|---|---|
| طلب | جديد → Draft | القسم الطالب؛ قسم نشط | حفظ الطلب وAuditLog | HTTP-06 |
| طلب | Draft/Returned → Under Review | صاحب الطلب؛ بند صالح وتاريخ مناسب | حفظ وإقفال التحرير؛ تدقيق | HTTP-07–09 للمسودة؛ مسار Returned ثابت |
| طلب | Under Review → Approved/Returned/Rejected | SourcingOfficer، مع قيد المسؤول المعيّن عند وجوده | قرار، ملاحظات، تاريخ في التدقيق وإشعار لصاحب الطلب | RequisitionReviewsController.Decide؛ لم يُشغّل |
| طلب | Approved → Converted to Tender | إنشاء عطاء draft ومعاملة Serializable | حفظ الرابط والحالة والتدقيق في نفس المعاملة | TendersController.Create |
| عطاء | Draft → Published/Open | Publish مع إغلاق مستقبلي ونافذة صحيحة | تحديث شرطي؛ لا إعداد إلزامي للمعايير | TendersController.Publish؛ B06 |
| عطاء | Published → Open | TenderClock عند موعد الفتح | تحديث دوري UTC | TenderClock؛ لم يُختبر الموعد |
| عطاء | Open/Published → Closed | انتهاء الموعد؛ Close يدوياً من Compliance وهو مفتوح | الحفظ؛ AuditLog للإغلاق المبكر فقط | B18؛ لم يُختبر التزامن |
| عطاء | Closed → Open | Reopen بعروض أقل من 3 | Counter=2 وتمديد الدعوات؛ حفظ العروض القائمة | B04/B05/B07 |
| عطاء | Closed → Under Evaluation | AssignCommittee بعد النصاب والمطابقة؛ أربعة أعضاء | تغيير الحالة وإسناد اللجنة وتدقيق | النصاب الحالي خاطئ، B04 |
| عطاء/عرض | Under Evaluation → Evaluation Closed | مدير التوريد بعد اكتمال درجات الأربعة | PDF وEvaluationReport بانتظار المسؤول | تقرير غير مركب B08 |
| عطاء | Evaluation Closed → Awaiting Manager Review | قبول المسؤول وتأكيد كلمة المرور | TenderApproval مستقل | ثابت؛ لا اختبار |
| عطاء | Awaiting Manager Review → Awaiting Committee Decision | المدير المعين مع أربعة موظفين TopCommittee نشطين | موافقة مستقلة وتثبيت أربعة مقاعد | ثابت؛ لا اختبار |
| عطاء | Awaiting Committee Decision → نفسه | تصويت عضو مؤهل؛ أول ثلاثة موافقات | سجل تصويت؛ منع تكرار صريح | EvaluationWorkflowController.Approve؛ E2E-08/09 |
| عطاء | Awaiting Committee Decision → Awarded | الموافقة الرابعة المتميزة | القرار والتوصية والإشعارات في معاملة واحدة | ثابت؛ B19؛ لم يُشغّل |
| عطاء | Awaiting Committee Decision → Cancelled | رفض أحد الأربعة | قرار وإشعار مدير التوريد والمشاركين | سبب غير إلزامي B09 |
| تقرير/طلب | Cancelled → Awaiting Handoff → Ready to Retender | المدير يعيد ثم المسؤول ينشئ عطاء جديداً | TenderApproval وإشعار؛ القديم محفوظ | TenderRules.CanCreate + AllowTenderHistory؛ E2E-06 لم يُشغّل |
| دعوة | Pending → Accepted/Declined | المورد صاحب الدعوة؛ داخل النافذة | وقت الرد؛ Expired عرض مشتق من الوقت | SupplierWorkspaceController.Respond؛ إعادة الإرسال B07 |
| عرض | جديد → Draft → Submitted | المورد المدعو المقبول؛ سعر البنود وملف وموعد | حفظ سعر إجمالي وتوقيت؛ لا تدقيق إرسال مستقل | BidsController.Create/Submit؛ B05 |
| عرض | Draft/Submitted → Withdrawn | المورد المالك قبل الإغلاق | تحديث الحالة | تضارب محتمل مع الإغلاق؛ E2E-09 |
| عرض | Submitted → Compliant/Non-Compliant | مسؤول التوريد بعد الإغلاق | سبب عند عدم المطابقة | لا Pending Clarification B10 |
| عرض | نتيجة القرار | أربعة موافقات/رفض | إشعار؛ Bid.Status لا يعكس نتيجة الترسية | B19 |
| استفسار | Pending → Answered → Resolved | المستلم ثم المرسل؛ صلاحية الرد مرتبطة بمرحلة المعاملة | سؤال/جواب وتواريخ وإشعار لكل طرف | ClarificationsController؛ لم يُعد اختبارها في هذا التدقيق |

| الدور | السماح الفعلي | فحص الحدود |
|---|---|---|
| Admin | حسابات، أقسام، موردون، مراقبة | لا تصويت بصفته Admin؛ تغيير الأدوار امتياز حساس. |
| RequestingDepartment | قراءة قسمه وتعديل طلبه فقط؛ مقعد تقييم عند إسناده | HTTP-03/09؛ فصل الأقسام لم يختبر ديناميكياً. |
| SourcingOfficer | مراجعة الطلبات والعطاءات والدعوات والمطابقة وقبول التقرير | قيد الدور موجود؛ حدود مالك العطاء غير موحدة B03/B11. |
| Supplier | دعواته وعروضه وملفاته | فحص المالك في Bids/BidItems/BidDocuments؛ منافس لم يختبر ديناميكياً. |
| TechnicalCommittee / FinancialCommittee | عروض مطابقة للعطاء المعيّن أثناء التقييم | CommitteeReviews.Reviewable؛ تحقق من فصل ما يُعرض لكل مقعد في الجولة الديناميكية. |
| SourcingManager | درجات المقعد ثم قرار إداري مستقل بعد المسؤول | لا احتساب تلقائي؛ دليل التسليم/التاريخ المالي B20؛ لا إزالة للمدير. |
| TopCommittee | تقرير العطاء المسند وتصويت حتى القرار | عضوية التصويت مفحوصة؛ تنزيل المرفقات لا يتبعها B03. |
| EvaluationCommittee القديم | دور Identity موجود وليس دور المقاعد الجديد في Reviewers | ترحيل الحسابات القديمة وإيضاح المسار في الإدارة. |

قرارات مطلوبة: تعريف العرض «الصالح» إذا كان غير مطابق أو منسحباً بعد الإرسال، ومصير عطاء بصفر بعد الجولة الثالثة (لا يسمح بالتقييم أو الترسية، لكن الإغلاق الإداري/إعادة الطرح يحتاج سياسة). قرارات Manager Reject/Return في V5 لا تظهر كمسارات فعلية، ولا يفترض التدقيق انتقالاً غير معتمد. يلزم تحديد تفويض المسؤول البديل، وسياسة انتهاء المشوار إذا تغيّر أحد أعضاء Top بعد تثبيت القائمة.
