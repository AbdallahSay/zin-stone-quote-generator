# 🏗️ ZIN STONE — Quotation Generator & Management Dashboard
### نظام إدارة وإصدار عروض الأسعار الذكي — شركة زين ستون لتجارة مواد البناء

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![ASP.NET Core Web API](https://img.shields.io/badge/ASP.NET_Core-Web_API-239120?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet)
[![PostgreSQL](https://img.shields.io/badge/Database-Supabase_PostgreSQL-3ECF8E?logo=supabase&logoColor=white)](https://supabase.com)
[![Supabase Storage](https://img.shields.io/badge/Storage-Supabase_Private_Bucket-3ECF8E?logo=supabase&logoColor=white)](https://supabase.com)
[![Netlify](https://img.shields.io/badge/Frontend-Netlify_Free-00C7B7?logo=netlify&logoColor=white)](https://www.netlify.com/)
[![Render](https://img.shields.io/badge/Backend-Render_Free_Docker-46E3B7?logo=render&logoColor=white)](https://render.com/)
[![GitHub Actions CI/CD](https://img.shields.io/badge/CI%2FCD-GitHub_Actions-2088FF?logo=github-actions&logoColor=white)](https://github.com/features/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## 🌟 Overview | نظرة عامة

**English:**  
**ZIN STONE Quotation Generator & Management Dashboard** is an enterprise-grade quotation management and dispatch system custom-engineered for **ZIN STONE — BUILDING MATERIALS TRADING**. Designed for zero-cost operation on reliable free tiers without requiring a credit card, it features a containerized ASP.NET Core Web API on **Render**, high-fidelity OpenXML and Linux LibreOffice PDF generation, PostgreSQL on **Supabase**, private cloud document storage via **Supabase Storage**, and a decoupled edge-hosted frontend on **Netlify**.

**العربية:**  
**منظومة إدارة وإصدار عروض الأسعار لشركة ZIN STONE** هي منصة متكاملة مخصصة لشركة **زين ستون لتجارة مواد البناء**. صُممت المنظومة للعمل بكفاءة على خدمات استضافة مجانية بالكامل **دون الحاجة لبطاقة بنكية (Credit Card)**، وتعتمد على محرك ASP.NET Core داخل حاوية Docker على **Render**، مع توليد مستندات Word و PDF مطابقة بدقة للهوية البصرية وقالب الشركة، وقاعدة بيانات PostgreSQL على **Supabase**، وتخزين سحابي خاص ومحمي للمستندات عبر **Supabase Storage**، وواجهة أمامية سريعة على **Netlify**.

---

## 🏛️ System Architecture | هيكل المنظومة

```text
┌─────────────────────────────────────────────────────────────────┐
│                    ZIN STONE Architecture                       │
└─────────────────────────────────────────────────────────────────┘
             │
             ├── 🌐 Frontend: Netlify Free
             │    ├── index.html   (Quotation Generator & Sales Interface)
             │    ├── admin.html   (Management, Analytics & Audit Logs)
             │    ├── owner.html   (Employee Account Provisioning)
             │    └── config.js    (Dynamic API Base URL Resolver)
             │
             ├── ⚙️ Backend: Render Free Web Service (Docker Container)
             │    ├── ASP.NET Core 10.0 Web API (0.0.0.0:${PORT})
             │    ├── ZinStoneWordQuoteGeneratorService (OpenXML Engine)
             │    ├── PdfConverterService (LibreOffice Linux Native & Fallbacks)
             │    ├── SupabaseDocumentStorageService (Private Document Bucket)
             │    └── WhatsAppDeliveryService (Modular / Future Meta Setup)
             │
             ├── 🗄️ Database: Supabase PostgreSQL Free
             │    ├── Npgsql EF Core 9.0 + Versioned Migrations
             │    └── Tables: Users, Clients, Products, Quotes, QuoteItems,
             │                AuditLogs, SystemCredentials, Outbox
             │
             └── 🔒 Storage: Supabase Storage Private Bucket
                  ├── Bucket: quotes-private
                  └── Authenticated Access with Scoped Capability Tokens
```

---

## 📄 ZIN STONE Document Template Fidelity | معايير القالب الرسمي

1. **Brand Identity:** Preserves the authentic ZIN STONE logo, gold branding palette, and original typography.
2. **Product Table Columns (4 أعمدة ثابتة):**
   - **النوع** (Product Type)
   - **المقاس** (Dimensions)
   - **الحمولة** (Capacity)
   - **سعر الألف** (Unit Price per 1,000 units)
   - *Formatting:* Headers are bold 13pt; body values are bold 11pt down to 8.5pt with automatic cell containment and RTL/LTR bidirectional markers (`\u200E`).
3. **Official Signature (توقيع الإدارة):**
   - Aligned to the far physical left side of the page.
   - Formatted into exactly two lines without abnormal spacing or disconnected letters:
     ```text
     المدير المسؤل
     يوسف زين الدين
     ```

---

## 🚀 Free-Tier Deployment Guide (No Credit Card)

### 1. Supabase (Database & Private Storage)
1. Sign up for free at [supabase.com](https://supabase.com/) using GitHub.
2. Click **New Project** and name it `zin-stone-db`.
3. In **Settings > Database > Connection string > URI**, copy the PostgreSQL URL:
   `postgresql://postgres:[YOUR-PASSWORD]@db.[REF].supabase.co:5432/postgres`
4. In **Storage**, click **New Bucket**:
   - Name: `quotes-private`
   - Public: **Disabled** (Private bucket)
5. In **Settings > API**, copy the `Project URL` and `service_role secret` key.
6. In **SQL Editor**, you can optionally run `database/supabase_init.sql` (the API also runs `EnsureCreated` automatically).

### 2. Render (Backend Docker Web Service)
1. Sign up for free at [render.com](https://render.com/) using GitHub.
2. Click **New > Web Service** > Connect your `AbdallahSay/zin-stone-quote-generator` repo.
3. Choose:
   - **Runtime:** `Docker`
   - **Plan:** `Free`
   - **Region:** `Frankfurt` (EU)
4. Add Environment Variables:
   | Variable | Value |
   |---|---|
   | `DATABASE_URL` | Your Supabase PostgreSQL URI |
   | `SUPABASE_URL` | `https://[YOUR-PROJECT-REF].supabase.co` |
   | `SUPABASE_SERVICE_KEY` | Your Supabase `service_role` secret |
   | `SUPABASE_BUCKET` | `quotes-private` |
   | `Security__SigningKey` | Random 32+ character HMAC key |
   | `Security__DefaultOwnerPassword` | Secure owner password for initial login |
   | `WhatsApp__Enabled` | `false` (until WhatsApp setup is guided) |
5. Click **Create Web Service**. Your API will be live at `https://zin-stone-api.onrender.com`.

### 3. Netlify (Frontend Static Edge)
1. Sign up for free at [netlify.com](https://www.netlify.com/) using GitHub.
2. Click **Add new site > Import an existing project > GitHub**.
3. Select `AbdallahSay/zin-stone-quote-generator`.
4. Configure:
   - **Publish directory:** `frontend`
5. Click **Deploy Site**. Netlify issues an immediate HTTPS domain (e.g. `https://zin-stone-quote-generator.netlify.app`).

---

## 💬 WhatsApp Integration Guide (Future Setup)

WhatsApp notifications are built into the system using `IWhatsAppDeliveryService`, kept safely disabled until the owner completes official Meta Cloud verification.

When ready to activate:
1. Obtain your **Phone Number ID** and **Permanent Access Token** from Meta for Developers.
2. In Render, add:
   ```env
   WhatsApp__Enabled=true
   WhatsApp__PhoneNumberId=<YOUR_META_PHONE_NUMBER_ID>
   WhatsApp__AccessToken=<YOUR_META_SYSTEM_USER_TOKEN>
   ```
3. Restart the service to begin automated delivery.

---

## 🔒 Security & Data Isolation | الأمان وحماية البيانات

- **Zero Cross-Company Exposure:** Completely independent repository, database, signing keys, and hosting.
- **Capability Tokens:** Private documents are protected by HMAC-SHA256 capability tokens (`?token=...`) with strict 7-day or 2-hour preview expiration.
- **Role-Based Isolation:** Sales employees cannot view or download quotations generated by other sales staff.
- **Audit Logging:** Every quotation creation, download, and login event is permanently audited with user identity and timestamp.

---

## 🧪 Testing & Verification | نتائج الاختبارات

All tests pass 100%:
- ✅ **102 Backend Security & Functional Tests:** Role-based authorization, sequence numbering, PDF/DOCX capability tokens, outbox retry handling, and document hash integrity.
- ✅ **Frontend XSS & DOM Injection Tests:** 10/10 test scenarios passing across admin, owner, and quotation screens.
- ✅ **PostgreSQL Migration Verification:** Successful EF Core 9.0 Npgsql migration generated.
- ✅ **Word & PDF Layout Fidelity:** Strict verification of 4 fixed columns, centering, bold styling, and left-aligned signature.

---

## 📜 License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
