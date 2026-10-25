# ZIN STONE — Supabase Database Backup, Export & Restore Guide

This document describes the backup, export, disaster recovery, and operational procedures for the **ZIN STONE** quotation system running on Supabase PostgreSQL Free Tier.

---

## 1. Automated Backups (Supabase Native)

Supabase automatically manages daily backups on all projects:
- **Location:** Supabase Dashboard > Project Settings > Database > Backups
- **Frequency:** Daily automated snapshots
- **Retention:** 7 days on the Free Tier
- **Point-in-Time Recovery:** Available as a manual snapshot restore in the Supabase UI.

---

## 2. Manual Backup / Export Procedure (pg_dump)

To take a full manual backup before making schema updates or quarterly audits:

### Option A: Using pg_dump (Command Line)
```bash
# Export schema + data to compressed archive
pg_dump "postgresql://postgres:[YOUR-PASSWORD]@db.[YOUR-PROJECT-REF].supabase.co:5432/postgres" \
  --format=c \
  --file="zin_stone_backup_$(date +%Y%m%d_%H%M%S).dump"

# Or plain SQL export (readable text)
pg_dump "postgresql://postgres:[YOUR-PASSWORD]@db.[YOUR-PROJECT-REF].supabase.co:5432/postgres" \
  --no-owner \
  --no-privileges \
  --file="zin_stone_backup_$(date +%Y%m%d_%H%M%S).sql"
```

### Option B: Exporting Tables via Supabase Dashboard
1. Log in to your Supabase project.
2. Navigate to **Table Editor**.
3. Select the target table (`Quotes`, `Users`, `Products`, `Clients`, `AuditLogs`).
4. Click **Export** > **Download CSV**.

---

## 3. Restore Procedure

### Option A: Full Restore via pg_restore
```bash
# Restore to Supabase or any standard PostgreSQL instance
pg_restore \
  --clean \
  --if-exists \
  --no-owner \
  --no-privileges \
  -d "postgresql://postgres:[YOUR-PASSWORD]@db.[YOUR-PROJECT-REF].supabase.co:5432/postgres" \
  "zin_stone_backup_YYYYMMDD_HHMMSS.dump"
```

### Option B: Reinitializing Schema from Scratch
If creating a completely new database instance:
1. Open the Supabase SQL Editor.
2. Copy and execute the contents of `database/supabase_init.sql`.
3. Start the ASP.NET Core API; it will automatically verify the schema and provision initial Owner credentials.

---

## 4. Free-Plan Operating Characteristics & Limits

| Characteristic | Render Free Web Service | Supabase PostgreSQL Free | Netlify Free Frontend |
|---|---|---|---|
| **Cost** | $0.00 / month (No card required) | $0.00 / month (No card required) | $0.00 / month (No card required) |
| **Inactivity Behavior** | Sleeps after 15 min of no traffic | Pauses after 7 days without queries | Always fast global CDN edge cache |
| **Wake-up Latency** | ~30–50 seconds on cold start | ~1–2 seconds (or 1 click unpause if paused) | Instant (0 ms) |
| **RAM / CPU** | 512 MB RAM, shared CPU | 500 MB DB size, 1 GB file storage | 100 GB bandwidth / month |
| **SLA / Uptime** | Best-effort (No 24/7 SLA) | Best-effort (No 24/7 SLA) | 99.99% CDN availability |

> [!NOTE]
> Because Render Free sleeps after 15 minutes of inactivity, the first user opening the quotation system in the morning may experience a brief 30–50 second loading delay while the container starts. Once warm, requests respond in under 200ms.
