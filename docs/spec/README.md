# Municipal Field Operations & Asset Integrity Platform
# منصة إدارة أصول وعمليات البلدية الميدانية

**وثيقة Product + Architecture Specification — الإصدار 0.1 (مسودة للمراجعة)**

> الحالة: **بانتظار الموافقة.** لا يوجد كود في هذه المرحلة عن قصد.
> أي بند هنا قابل للنقاش، لكن التعديل يجب أن يمر عبر ADR (Architecture Decision Record) وليس تعديلًا صامتًا — نطبق على الوثيقة نفس فلسفة النظام.

---

## كيف تُقرأ هذه الوثيقة

القارئ صاحب القرار (مدير/راعي المشروع): اقرأ **01** ثم **16** ثم **15**.
القارئ التقني: اقرأ الكل بالترتيب.

| # | الملف | يغطي البنود المطلوبة |
|---|------|------|
| 01 | [الملخص التنفيذي والتحليل النقدي](01-executive-summary-and-critique.md) | 1. Executive Summary · 2. Critical analysis |
| 02 | [تحليل التهديدات وحالات الإساءة](02-threat-and-abuse-analysis.md) | 3. Threat / Abuse Case Analysis |
| 03 | [الأدوار ونموذج الصلاحيات](03-roles-and-permissions.md) | 4. User roles · 12. Permission model |
| 04 | [دورة حياة الأصل والتحقق منه](04-asset-lifecycle-and-verification.md) | 5. Workflows (أصول) · 13. Asset verification design |
| 05 | [أوامر العمل](05-work-orders.md) | 5. Workflows (صيانة) · 14. Work order state machine |
| 06 | [المخزون](06-inventory.md) | 5. Workflows (مخزون) · 15. Inventory state machine |
| 07 | [سلسلة الأدلة، التدقيق، وضوابط مكافحة الفساد](07-evidence-audit-integrity.md) | 11. Audit architecture · 16. Evidence chain · 17. Anti-corruption controls |
| 08 | [المعمارية والقرارات التقنية](08-architecture-and-technology.md) | 7. Architecture · 9. API boundaries · 27. Folder structure · 28. Technology decisions |
| 09 | [نموذج البيانات](09-domain-data-model.md) | 8. Database/domain model |
| 10 | [استراتيجية العمل دون اتصال](10-offline-sync.md) | 10. Offline sync strategy |
| 11 | [قواعد التحليلات](11-analytics-rules.md) | 18. Analytics rules |
| 12 | [هيكل تجربة المستخدم](12-ux-structure.md) | 19. UX structure |
| 13 | [الأمن والخصوصية والنشر](13-security-privacy-deployment.md) | 20. Deployment plan · 21. Security plan |
| 14 | [استراتيجية الاختبار](14-testing-strategy.md) | 22. Testing strategy |
| 15 | [المراجعة النقدية الثانية (Red Team) والتعديلات](15-red-team-review.md) | المراجعة النقدية المطلوبة + التصميم المعدل |
| 16 | [المراحل، تعريف الإنجاز، المخاطر، وما لن يُبنى](16-roadmap-phases-risks.md) | 6. MVP scope · 23. Phases · 24. DoD · 25. Risks · 26. What NOT to build |

## المبادئ الحاكمة (Design Philosophy) — كما ستُطبق فعليًا

| المبدأ | ماذا يعني عمليًا في هذا التصميم |
|---|---|
| Integrity by Design | الحالة الرسمية تتغير فقط عبر أوامر (Commands) تمر بـ state machine على الخادم. لا يوجد `UPDATE` حر على سجل تشغيلي. |
| Evidence by Default | كل انتقال حساس يتطلب أدلة محددة مسبقًا لنوع الأصل ونوع المهمة، ومستوى الأدلة يتناسب مع مستوى المخاطرة. |
| Least Privilege | الصلاحية = دور + نطاق (قسم/منطقة) + قواعد على مستوى السجل. |
| Separation of Duties | قواعد SoD مفروضة في الكود (ثابتة وديناميكية)، ليست مجرد توصية. |
| Auditability | كل تغيير يكتب Audit Event **في نفس المعاملة** (Transaction) مع التغيير. سلسلة Hash تجعل العبث قابلًا للكشف. |
| Offline First | التطبيق الميداني يعمل بالكامل دون اتصال عبر صندوق أوامر محلي (Outbox) — الخادم هو الحكم النهائي. |
| Arabic First | RTL، مصطلحات ميدانية عربية حقيقية، أرقام وتواريخ قابلة للتهيئة. |
| Low-resource Friendly | هواتف Android متوسطة، شبكات ضعيفة، صور مضغوطة ذكيًا، خوادم متواضعة. |
| API First | الواجهات (Web/Mobile/Integrations) كلها تستهلك نفس الـ API الموثق (OpenAPI). |
| Human Final Authority | النظام يرفع **إشارات** (Flags)، ولا يصدر أحكامًا. كل قرار عقابي أو مالي يتخذه إنسان مسجّل. |

## ما لا يدعيه هذا النظام

**لا يمنع الفساد بنسبة 100%.** من يملك الأجهزة والخوادم والتواطؤ الكافي يستطيع التلاعب.
الهدف القابل للقياس هو:

1. رفع **تكلفة** التلاعب (أدلة أكثر، أشخاص أكثر يجب أن يتواطؤوا).
2. جعل التلاعب **قابلًا للكشف** لاحقًا (لا يوجد تغيير بلا أثر).
3. **كشف الشذوذ مبكرًا** وتسهيل عمل المراجع البشري.
4. **حماية الموظف النزيه** — الأدلة تثبت أنه أنجز عمله.

النقطة الرابعة مهمة جدًا لقبول النظام من الفنيين، وهي مشروحة في [01](01-executive-summary-and-critique.md).
