# Municipal Field Operations & Asset Integrity Platform
# منصة إدارة أصول وعمليات البلدية الميدانية

(اسم مؤقت — ليس الاسم التجاري النهائي)

طبقة عمليات ميدانية للبلديات: تعطي كل أصل عام (عمود إنارة، مضخة، طريق...) سجلًا رقميًا موثقًا بالأدلة، وتجعل كل عملية صيانة وكل صرف مواد قابلًا للتتبع والتدقيق.

## الحالة

**المرحلة الحالية: مواصفات المنتج والمعمارية — بانتظار الموافقة قبل التنفيذ.**

📄 [وثيقة Product + Architecture Specification](docs/spec/README.md)

## البنية المخططة للمستودع

```
docs/      المواصفات، قرارات المعمارية (ADRs)، أدلة التشغيل
backend/   ASP.NET Core — Modular Monolith
web/       React + TypeScript — لوحة الإدارة (RTL)
mobile/    Flutter — التطبيق الميداني (Offline-first)
deploy/    Docker Compose، إعدادات البنية
tools/     البذور، طباعة ملصقات QR، أداة التحقق من سلسلة التدقيق
```

(المجلدات عدا `docs/` تُنشأ في Phase 0 بعد الموافقة.)
