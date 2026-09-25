# 07 — سلسلة الأدلة، معمارية التدقيق، وضوابط مكافحة الفساد

## 7.1 سلسلة الأدلة (Evidence Chain)

### الدليل ككيان

```
Evidence
  id (UUID, يُنشأ على الجهاز)
  work_order_id? / asset_id? / registration_id? / count_session_id?
  kind            : PHOTO_BEFORE | PHOTO_AFTER | PHOTO_TEST | PHOTO_TAG | PHOTO_CORE
                    | PHOTO_ASSET_REF | DOCUMENT | ...
  capture:
    sha256            (يُحسب على الجهاز لحظة الالتقاط، على البايتات الأصلية)
    device_id, app_version, user_id
    device_wall_time, elapsed_realtime_ms, boot_id
    gps: {lat, lon, accuracy_m, provider, is_mock, fix_age_ms}
    camera_source     : IN_APP_CAMERA   (القيمة الوحيدة المقبولة لأدلة العمل)
  server:
    received_at, estimated_capture_time, clock_skew_ms
    storage_key (WORM bucket), size, mime (مُتحقق)
    phash (64-bit perceptual hash)
    verification: HASH_OK | HASH_MISMATCH | PENDING_UPLOAD
  status          : ACTIVE | SUPERSEDED(by, reason)
```

### من الالتقاط إلى الختم

```
[الجهاز]
 كاميرا التطبيق ← بايتات JPEG ← sha256 ← حفظ مشفر محليًا
 ← أمر "AddEvidence" في صندوق الأوامر (يحمل الـ hash والبيانات الوصفية، لا الصورة)
 ← رفع الصورة لاحقًا (قابل للاستئناف)

[الخادم]
 استلام الأمر → سجل Evidence بحالة PENDING_UPLOAD + حدث تدقيق فيه الـ hash
 استلام الملف → حساب sha256 → مطابقة؟
     نعم → نقل إلى Bucket بـ Object Lock (WORM) → HASH_OK
     لا  → HASH_MISMATCH → Flag حرج (تعديل بعد الالتقاط أو تلف)
 → مهمة خلفية: pHash + بحث التشابه + مصغرات (Thumbnails) مُعاد ترميزها بلا EXIF

[عند الإغلاق]
 Seal = sha256( canonical_json( أمر العمل النهائي + قائمة (evidence_id, sha256)
                               + حركات المواد + نتائج الفحص + القرارات ) )
 → يُكتب Seal في سجل التدقيق (وبالتالي في سلسلة الـ Hash)
```

**لماذا الـ hash في الأمر قبل الملف؟** لأن الأمر (صغير) يصل عادةً قبل الصورة (كبيرة). بمجرد وصول الأمر، الـ hash "مُلتزم به" في سجل التدقيق — أي استبدال للصورة لاحقًا (حتى من الفني نفسه على جهازه) سيُكشف.

**لماذا Object Lock (WORM)؟** حتى مدير التخزين لا يستطيع حذف أو استبدال الأصل خلال فترة الاحتفاظ. (MinIO/Ceph/S3 تدعم Compliance mode.)

**المرحلة 2: توقيع الجهاز.** مفتاح خاص يُولد في Android Keystore (مدعوم بالعتاد) عند تسجيل الجهاز؛ كل دليل وأمر يُوقَّع. هذا يثبت أن الدليل صدر من **تطبيقنا على جهاز مسجل** — لا من سكربت يستدعي الـ API. مؤجل لأن MVP يعتمد على أجهزة محدودة ويمكن تعويضه بـ Device binding للـ Token.

### تسلسل أدلة أمر العمل (Chain of Evidence)

```
WorkOrder ─► Asset(version at time) ─► Assignment(who, by whom, when)
 ─► Arrival (tag scan, GPS, time) ─► Before photos (hash) ─► Diagnosis
 ─► Start ─► Material consumption (custody movements, serials)
 ─► Checklist result (template version) ─► Test evidence ─► After photos
 ─► Core return declaration ─► Report ─► Submit(s)
 ─► Review decision(s) ─► [Second approval] ─► CLOSED + Seal
 ─► [Post-closure amendments — each with its own approval & seal]
```

كل عنصر يشير لسابقه، ولكل عنصر حدث تدقيق. عرض "سلسلة الأدلة" في واجهة المدقق = خط زمني واحد لكل ما سبق مع علامة تحقق لكل عنصر (✓ hash، ✓ موقع، ⚠ انحراف ساعة ...).

### Post-Closure Modification

```
AmendmentRequest (ApprovalRequest نوع POST_CLOSURE_AMENDMENT)
  target: WO + field/section
  old_value (من النسخة المختومة), new_value, reason, attachments
  → Checker (مدير قسم ≠ الطالب)
  → عند الموافقة: WorkOrderAmendment جديد + Seal جديد يشير للسابق
  → أمر العمل يحمل علامة دائمة "مُعدَّل بعد الإغلاق (n)"
```
القراءة الافتراضية تعرض القيم **بعد** التعديلات، مع تمييز بصري وإمكانية رؤية الأصل.

## 7.2 إثبات الحضور (Proof of Presence)

لا إشارة منفردة قطعية. نحسب **درجة ثقة حضور** شفافة لكل وصول (Arrival):

| الإشارة | القيمة | ملاحظة |
|---|---|---|
| مسح QR للأصل الصحيح | +30 | يمكن نسخه — لذلك ليس كافيًا |
| الـ Tag ظاهر في صورة Before (يؤكده المراجع أو لاحقًا آليًا) | +20 | |
| GPS ضمن `max(25م, 2×accuracy)` من الأصل | +25 | |
| GPS دقة ≤ 20م وليس Mock | +10 | |
| ساعة موثوقة (انحراف < 2 دقيقة) | +10 | |
| جهاز مسجل ومرتبط بالمستخدم | +5 | |
| **سالب:** is_mock=true | −40 | |
| **سالب:** مسح يدوي للرقم بدل QR | −15 | مع سبب |
| **سالب:** زمن التنقل من الموقع السابق غير واقعي (> 120 كم/س) | −30 | |

- `≥ 70` قوي · `40–69` مقبول · `< 40` ضعيف ← يرفع مستوى مخاطرة أمر العمل.
- **الأوزان مبدئية** وستُعاير في التجربة. تُعرض مفصلة للمراجع (لا رقم فقط).
- لاحقًا: NFC (+30) يعوض ضعف QR.

## 7.3 معمارية سجل التدقيق (Audit Architecture)

### المتطلبات

1. **لا تغيير بلا حدث:** الحدث يُكتب في **نفس معاملة قاعدة البيانات** مع التغيير. إذا فشل أحدهما، فشل الاثنان. (البديل — كتابة التدقيق بشكل غير متزامن عبر Queue — يسمح بتغيير بلا أثر عند فشل الـ Queue. مرفوض.)
2. **Append-only** على مستوى قاعدة البيانات.
3. **كشف العبث** بسلسلة Hash + تثبيت خارجي.
4. **قابل للاستعلام** بسرعة (حسب الكيان، المستخدم، الفترة).
5. **لا يحتوي بيانات شخصية زائدة** — يشير إليها (يسهل الامتثال لسياسات الاحتفاظ دون كسر السلسلة).

### الحدث

```
audit_events (partitioned by month)
  seq            BIGINT   (تسلسل صارم لكل tenant — يُخصص تحت قفل)
  event_id       UUID
  tenant_id
  occurred_at    (وقت الخادم)
  actor_user_id, actor_role, on_behalf_of?, break_glass_session_id?
  device_id?, ip?, user_agent_hash?
  action         (مثل: workorder.review.approved, inventory.movement.created)
  entity_type, entity_id, entity_version
  changes        JSONB  [{field, old, new}]   ← القيم التشغيلية فقط
  reason?        TEXT
  correlation_id (الطلب/الأمر الأصلي)، command_id (من الجهاز)
  payload_hash   = sha256(canonical(event without hashes))
  prev_hash      = hash للحدث seq-1
  hash           = sha256(prev_hash || payload_hash)
```

### الحماية على مستوى قاعدة البيانات

```sql
-- مستخدم التطبيق: INSERT و SELECT فقط
REVOKE UPDATE, DELETE, TRUNCATE ON audit_events FROM app_user;
-- محفز يرفض أي UPDATE/DELETE حتى من المالك (يكشف أي محاولة في السجلات)
CREATE TRIGGER audit_events_immutable BEFORE UPDATE OR DELETE ON audit_events
  FOR EACH ROW EXECUTE FUNCTION raise_immutable_violation();
```
- مستخدم الترحيل (Migrations) منفصل ولا يُستخدم وقت التشغيل.
- صلاحية superuser محصورة، وتسجيل الدخول بها عبر إجراء Break Glass تشغيلي.
- `pgaudit` لتسجيل أوامر DDL/DML المباشرة خارج التطبيق (إلى ملفات تُشحن لنظام السجلات).

### التثبيت الخارجي (External Anchoring)

```
كل 24 ساعة (وكل N=10,000 حدث):
  checkpoint = {tenant, from_seq, to_seq, last_hash, count, merkle_root?, at}
  signature  = sign(checkpoint, anchor_key)   ← مفتاح لا يملكه فريق التشغيل (HSM/KMS أو مفتاح لدى جهة التدقيق)
  يُكتب إلى:
    1) Bucket WORM منفصل (حساب/Bucket مختلف عن الأدلة)
    2) بريد/قناة يومية لجهة الرقابة الداخلية (نسخة لا يتحكم بها فريق التقنية)
```

**التحقق:** مهمة يومية + أداة للمدقق "تحقق من السلسلة" تعيد حساب الـ Hashes من البداية أو من آخر Checkpoint، وتقارن بالتثبيت الخارجي. أي كسر = حادثة نزاهة حرجة.

**فحص الاتساق الليلي (يسد ثغرة "تعديل الجداول التشغيلية دون لمس التدقيق"):**
- إعادة حساب Seal لكل أمر عمل أُغلق خلال آخر 24 ساعة + عينة عشوائية من الأقدم، **من الجداول التشغيلية**، ومقارنته بالـ Seal المسجل في التدقيق.
- مقارنة `stock_balances` بمجموع `stock_movements` لكل موقع وصنف.
- مقارنة النسخة الحالية لكل أصل معدل بآخر `asset_versions.snapshot_hash` معتمد.
- أي عدم تطابق = حادثة نزاهة حرجة (تنبيه للمدقق، لا إصلاح آلي).

**مبدأ: الخام مقدس، المشتقات قابلة لإعادة الحساب.** كل رقم مشتق (الوقت المقدّر، درجة الحضور، التكلفة، Health Score) يُحسب من بيانات خام محفوظة. إذا تبين خطأ في معادلة، نعيد الحساب ونسجل حدثًا بذلك — دون تعديل الخام.

**حدود صريحة:** المهاجم الذي يملك قاعدة البيانات **ومفتاح التثبيت** و**قناة الرقابة** يستطيع إعادة كتابة التاريخ. هذا مقصود: تكلفة ذلك (3 جهات) عالية جدًا.

### ما لا يدخل سجل التدقيق كاملًا

- الصور: يدخل الـ hash فقط.
- المسار الكامل للـ GPS: لا يُجمع أصلًا.
- البيانات الشخصية القابلة للحذف: يدخل مرجع (ID) لا القيمة. عند انتهاء الاحتفاظ، تُحذف القيمة من جدولها ويبقى الحدث سليمًا.

## 7.4 ضوابط مكافحة الفساد — الخريطة الكاملة

| الطبقة | الضابط | يعالج |
|---|---|---|
| **الهوية** | ملصقات كعهدة · تحقق ثنائي للأصول · عينة ميدانية · أصل جديد يتطلب مشروعًا | الأصول الوهمية |
| **الأدلة** | كاميرا داخلية · hash عند الالتقاط · WORM · pHash · Tag في الإطار · درجة حضور | الإصلاح الوهمي، إعادة الصور |
| **العملية** | State machine على الخادم · لا إلغاء بعد الأدلة · لا إعادة فتح · Amendments بموافقة | التعديل الصامت |
| **الفصل** | SoD ثابت وديناميكي · Four-Eyes · منع منح الذات | الفرد المنفرد |
| **المخزون** | Ledger · تسليم ثنائي · عهدة بحد · Core Return · جرد أعمى ومفاجئ · أرقام تسلسلية | سرقة المواد |
| **الاستثناء** | Break Glass محدود ومراقب | إساءة الصلاحيات الطارئة |
| **الكشف** | قواعد تحليلية · Random Audit · زيارات ميدانية · تحليل المراجعين | ما يفلت من المنع |
| **السجل** | Audit in-transaction · Hash chain · External anchor | عبث المسؤولين |
| **التنظيم** | مدقق مستقل · تدوير · جولات من فريق مستقل | التواطؤ |

## 7.5 التدقيق العشوائي (Random Audit)

```
يوميًا (مهمة مجدولة):
  population = أوامر العمل المغلقة أمس
  sample     = CSPRNG اختيار:
                 5%  من LOW/STANDARD
                 15% من HIGH
                 + 100% مما عليه Flag حرج لم يُحل
               (وزن إضافي لمن لم يُدقق عمله منذ مدة — يضمن تغطية كل فني)
  لكل عينة: AuditCase → طابور المدقق
     مراجعة مكتبية: الصور، المواد، الأوقات، الموقع، الفحص، سلسلة الأدلة
     → نتيجة: CONFIRMED_OK | MINOR_ISSUE | NEEDS_FIELD_VISIT | CONFIRMED_ISSUE
  NEEDS_FIELD_VISIT → مهمة زيارة ميدانية (مفتش من خارج الفريق):
     هل الأصل موجود؟ يعمل؟ القطعة المركبة تطابق الرقم التسلسلي؟ (يمكن تصوير الكشاف من الأسفل)
```

- **الاختيار يتم بعد الإغلاق** ولا يظهر أي أثر له للفني أو المشرف.
- البذرة العشوائية والقائمة المختارة تُسجل في سجل التدقيق (تثبت أن الاختيار لم يُتلاعب به لاحقًا).
- نتيجة التدقيق **تُغذي معايرة القواعد**: أوامر عمل وجدها التدقيق سليمة بينما القاعدة علّمتها ← إيجابيات كاذبة.

## 7.6 تحليل المشرفين والمديرين (Phase 2)

| الإشارة | الحساب | لماذا |
|---|---|---|
| زمن المراجعة المنخفض | وسيط ثواني المراجعة لكل أمر عمل مقابل عدد الأدلة | Rubber stamping |
| نسبة الإرجاع | % أوامر مُرجعة؛ 0% لفترة طويلة مريب، وكذلك 60% | |
| نتائج التدقيق العشوائي على ما اعتمده | % CONFIRMED_ISSUE في عينة موافقاته | **أقوى مؤشر — مبني على تحقق فعلي** |
| الاستثناءات | تجاوزات للحدود، موافقات على HIGH | |
| Amendments بعد الإغلاق | عدد وطبيعة | |
| Break Glass | عدد، مدة، نطاق | |
| تركّز الإسناد | معامل جيني لتوزيع المهام/الساعات الإضافية على الفريق | المحاباة |

**حد أدنى:** لا تُحسب أي إشارة شخصية بأقل من 30 قرارًا في الفترة.
**العرض:** للمدقق فقط، مع البيانات الخام، ودون ترتيب "أسوأ 10 موظفين" (Ranking) — لأن الترتيب يحوّل الإشارة إلى حكم.

## 7.7 الإشارة (Flag) كنموذج

```
AuditFlag
  rule_code, rule_version, severity
  subject: (entity_type, entity_id)   ← أمر عمل / أصل / حركة  — لا "شخص" في MVP
  related_users[] (للسياق فقط)
  explanation: {inputs, thresholds, computed_values}   ← لماذا بالضبط
  status: OPEN → UNDER_REVIEW → CLOSED(CONFIRMED | NOT_CONFIRMED | ACCEPTABLE_EXPLANATION)
  employee_response?   ← حق الرد
  reviewer, decision_reason
```

**الصياغة في الواجهة:** "شذوذ يحتاج مراجعة: استهلاك الكابل (8م) أعلى من 95% من المهام المماثلة (المتوسط 2م، عينة 143)". لا كلمات "مشبوه"، "احتيال"، "مخالف".
