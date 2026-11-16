# تتبع المتطلبات

38 مجموعة متطلبات قابلة للتتبّع، مجمعة من الطلب المحدّث ومن V5؛ العدد يحسب المجموعات لا كل جملة في المرجع. «متحقق» نطاقه المحدد في الصف، ولا يثبت دورة كاملة. «جزئي» يعني وجود تنفيذ ثابت و/أو عينة تشغيل مع أعمال معلقة. عند تعارض المرجع مع التوضيح الأخير يُقدّم التوضيح.

| ID | المتطلب | مكان التنفيذ | الحالة | الدليل والمتبقي |
|---|---|---|---|---|
| R01 | إنشاء وتحرير مسودة وبنود وإرسال | PurchaseRequisitions/RequisitionItems | متحقق | HTTP-06–09 |
| R02 | قرارات الموافقة والإرجاع والرفض والتاريخ | RequisitionReviews | جزئي | E2E-02/03 |
| R03 | ملكيات الأقسام والموظفين | RequisitionBase / PurchaseRequisitions | جزئي | HTTP-03/09 |
| R04 | إنشاء العطاء من طلب معتمد وربط المعاملة | Tenders.Create | جزئي | E2E-01/03 |
| R05 | وصف الطلب المستقل | RequisitionInputs | مفقود | B22 |
| R06 | نشر وتوقيت وإغلاق | Tenders/Compliance/TenderClock | جزئي | B18؛ E2E-09 |
| R07 | المعايير قبل النشر والأوزان | CommitteeReviews | متحقق آليًا | [المرحلة الثالثة](PHASE_3_EVALUATION_AWARD_FIX_REPORT.md) |
| R08 | تسجيل المورد ووثائق واعتماد | SupplierRegistration/Suppliers | جزئي | E2E-01 |
| R09 | دعوات وقبول ورفض ومدة | Invitations/SupplierWorkspace | جزئي | B07 |
| R10 | إنشاء وتسعير وإرسال وسحب عرض | Bids/BidItems/BidDocuments | جزئي | B05 |
| R11 | سرية العروض والملفات | Bids/BidDocuments | جزئي | B03 |
| R12 | ثلاث جولات ثم عرض صالح واحد أو أكثر | TenderRules/Reopen | جزئي | B04؛ BID-01–08 |
| R13 | موردون متميزون ومنع عروض مكررة | TenderRules/DbContext | جزئي | B05 |
| R14 | سجل الجولات وإعادة الدعوة | Invitations | جزئي | B07 |
| R15 | المطابقة وسبب الاستبعاد | Compliance | متحقق آليًا | [المرحلة الثالثة](PHASE_3_EVALUATION_AWARD_FIX_REPORT.md) |
| R16 | Pending Clarification في المطابقة | Compliance/Clarifications | متحقق آليًا | [المرحلة الثالثة](PHASE_3_EVALUATION_AWARD_FIX_REPORT.md) |
| R17 | تشكيل أربعة مقيّمين وعزلهم | Tenders/CommitteeReviews | جزئي | E2E-01/07 |
| R18 | درجات فنية ومالية وملاحظات وتكاليف | CommitteeReviews | متحقق آليًا | [المرحلة الثالثة](PHASE_3_EVALUATION_AWARD_FIX_REPORT.md) |
| R19 | إرسال تقييم فردي بتوقيت وتدقيق | CommitteeReviews | متحقق آليًا | [المرحلة الثالثة](PHASE_3_EVALUATION_AWARD_FIX_REPORT.md) |
| R20 | تقرير موزون وموحد | EvaluationWorkflow.Upload | متحقق آليًا | [المرحلة الثالثة](PHASE_3_EVALUATION_AWARD_FIX_REPORT.md) |
| R21 | قبول المسؤول ثم موافقة المدير المستقلة | EvaluationWorkflow.Approve | متحقق آليًا | [المرحلة الثالثة](PHASE_3_EVALUATION_AWARD_FIX_REPORT.md) |
| R22 | Manager Return/Reject من V5 | EvaluationWorkflow | مفقود | سياسة انتقال غير محددة |
| R23 | قرار إجماع لأربعة ثابتين | EvaluationWorkflow.Approve | جزئي | VOTE |
| R24 | رفض واحد وإلزام السبب | EvaluationWorkflow.Approve | متحقق آليًا | [المرحلة الثالثة](PHASE_3_EVALUATION_AWARD_FIX_REPORT.md) |
| R25 | إشعارات الفائز والآخرين | EvaluationWorkflow/Notifications | متحقق آليًا | [المرحلة الثالثة](PHASE_3_EVALUATION_AWARD_FIX_REPORT.md) |
| R26 | حفظ العطاء الملغى وإعادة الطرح 1:N | AllowTenderHistory/Tenders | جزئي | E2E-06 |
| R27 | الإدارة والحسابات وإعادة التعطيل | Employees/Departments/Suppliers | جزئي | E2E-07 |
| R28 | المؤشرات والبحث وتصدير PDF/Excel | AdminOperations والإحصاءات | جزئي | B13 |
| R29 | بوابة عامة ومعلومات واتصال | Views/Home | جزئي | B21 |
| R30 | وجه وحيوية وحساب وPasskeys | Account/BiometricService | جزئي | B01/B02؛ BIO |
| R31 | Ollama واستخراج ومقارنة وتوصية | BidAiAdvisor | غير مختبر | AI؛ B15 |
| R32 | البحث الصوتي والبديل النصي | department-request-voice-search.js | جزئي | UI؛ B16 |
| R33 | الاستفسارات بالأدوار والأطراف والتوقيت | ClarificationRouting/Clarifications | جزئي | E2E-07/10 |
| R34 | إشعارات الاستفسارات وسجلها | Notifications/Clarifications | جزئي | E2E-10 |
| R35 | QR | _RecordQr | جزئي | HTTP-10 (عرض فقط)؛ CRYPTO |
| R36 | توقيع PDF وعلامة مائية حسب المستخدم | ProtectedPdf | غير مختبر | CRYPTO؛ اعتماد شهادة الاختبار غير منفذ هنا |
| R37 | CSRF لحفظ طلب الشراء | Program / PurchaseRequisitions.Create | متحقق | HTTP-05 |
| R38 | AJAX/Modal وإمكانية الوصول | workspace.js/_Layout | جزئي | HTTP-04؛ UI |

مرجع V5 المتاح: `C:/Users/user/Downloads/Sourcing_Tender_Management_System_English_Brief_V5 (1).docx`. تُراجع متطلبات البوابة (§5)، التحليلات (§7)، الكيانات (§9) والميزات الإلزامية (§10–11) مع تقارير الواجهة والأمن. الدعوات تمثّل علاقة المورد بالعطاء بدلاً من كيان TenderSuppliers، لكنها لا تحفظ كل جولة مستقلة. قرار إعادة التقرير للمدير عند الرفض يجب ألا يخلط درجته الفنية بموافقته؛ الفصل موجود بنيوياً، وتوقيت تسليم تقييمه لا يزال ناقصاً.

## نطاق تحقق المرحلة الرابعة

تم التحقق في Chrome من مسار القسم إلى Under Review، ودخول الأدوار الأساسية، وتسجيل ودخول Passkey بمصدّق افتراضي، وربط واجهة الصوت بمحاكاة. تظل دورة المتصفح الكاملة A/B/C وتكاملات الأجهزة والتقارير غير مكتملة. [الأدلة](PHASE_4_END_TO_END_REPORT.md).
