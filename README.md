# Municipal Field Operations & Asset Integrity Platform
# منصة إدارة أصول وعمليات البلدية الميدانية

(اسم مؤقت — ليس الاسم التجاري النهائي)

طبقة عمليات ميدانية للبلديات: تعطي كل أصل عام (عمود إنارة، مضخة، طريق...) سجلًا رقميًا موثقًا بالأدلة، وتجعل كل عملية صيانة وكل صرف مواد قابلًا للتتبع والتدقيق.

## الحالة

**المرحلة الحالية: M2 (تطبيق أندرويد: المسح ومهامي + الأعطال وأوامر العمل + اعتماد الأجهزة) مكتملة — بانتظار الموافقة قبل M3.**
التفاصيل: [docs/status/IMPLEMENTATION_STATUS.md](docs/status/IMPLEMENTATION_STATUS.md)

- 📄 [المواصفات طويلة المدى](docs/spec/README.md) — معتمدة
- 🧪 [Lean Pilot Specification](docs/lean-pilot/README.md) — معتمدة (DEMO 1 في [11](docs/lean-pilot/11-milestones-and-dod.md))
- 🧭 [قرارات المعمارية (ADRs)](docs/adr/)
- 🤖 [قواعد العمل للمطورين ووكلاء الذكاء الاصطناعي](AGENTS.md)

## التشغيل السريع

```bash
make up      # يولّد .env بأسرار عشوائية، يبني ويشغّل: db, keycloak, migrate, api, web
make e2e     # دخول كل مستخدم تجريبي + تسجيل عمود واعتماده وطباعة QR
make verify-audit  # التحقق المستقل من سلسلة التدقيق (tamper-evident)
make android-e2e   # تطبيق الهاتف على جهاز أندرويد موصول (adb reverse)
make test    # اختبارات backend + web + mobile
```

الواجهة: http://localhost:5173 — التفاصيل في [docs/runbooks/dev-setup.md](docs/runbooks/dev-setup.md).

## بنية المستودع

```
docs/      المواصفات، ADRs، آلات الحالات (YAML)، أدلة التشغيل، حالة التنفيذ
backend/   ASP.NET Core (.NET 10) — Modular Monolith + اختبارات
web/       React + TypeScript — لوحة الإدارة (عربي، RTL)
mobile/    Flutter — التطبيق الميداني (Android)
deploy/    Docker Compose، Keycloak realm، تهيئة PostgreSQL، بيانات المستخدمين التجريبيين
tools/     audit-verify (أداة التحقق من سلسلة التدقيق)، أدوات التطوير
```
