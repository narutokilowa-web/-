# Municipal Field Operations & Asset Integrity Platform
# منصة إدارة أصول وعمليات البلدية الميدانية

(اسم مؤقت — ليس الاسم التجاري النهائي)

طبقة عمليات ميدانية للبلديات: تعطي كل أصل عام (عمود إنارة، مضخة، طريق...) سجلًا رقميًا موثقًا بالأدلة، وتجعل كل عملية صيانة وكل صرف مواد قابلًا للتتبع والتدقيق.

## الحالة

**المرحلة الحالية: مواصفات Lean Pilot — بانتظار الموافقة قبل كتابة أي كود.**

- 📄 [المواصفات طويلة المدى (Product + Architecture Specification)](docs/spec/README.md) — معتمدة
- 🧪 [Lean Pilot Specification — Street Light Vertical Slice](docs/lean-pilot/README.md) — بانتظار الموافقة

## البنية المخططة للمستودع

```
docs/      المواصفات، قرارات المعمارية (ADRs)، أدلة التشغيل
backend/   ASP.NET Core — Modular Monolith
web/       React + TypeScript — لوحة الإدارة (RTL)
mobile/    Flutter — التطبيق الميداني (Offline-first)
deploy/    Docker Compose، إعدادات البنية
tools/     البذور، طباعة ملصقات QR، أداة التحقق من سلسلة التدقيق
```

(المجلدات عدا `docs/` تُنشأ في M0 من الـ Lean Pilot بعد الموافقة.)
