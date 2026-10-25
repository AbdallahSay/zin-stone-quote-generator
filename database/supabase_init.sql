-- ==============================================================================
-- ZIN STONE - Building Materials Trading
-- Supabase PostgreSQL Database Initialization Script
-- 
-- System: Quotation Generator & Management Dashboard
-- Isolated Database Environment for ZIN STONE
-- ==============================================================================

-- Enable UUID extension if needed
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- 1. System Credentials (Stores password hashes & session salts for admin, owner, quote roles)
CREATE TABLE IF NOT EXISTS "SystemCredentials" (
    "Role" VARCHAR(50) PRIMARY KEY,
    "PasswordHash" TEXT NOT NULL,
    "SessionSalt" VARCHAR(100) NOT NULL,
    "UpdatedAt" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 2. Users (Employees, Admins, Owners)
CREATE TABLE IF NOT EXISTS "Users" (
    "Id" SERIAL PRIMARY KEY,
    "FullName" VARCHAR(200) NOT NULL,
    "Email" VARCHAR(255) NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "Role" VARCHAR(50) NOT NULL DEFAULT 'Sales',
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");

-- 3. Clients (Customers receiving quotations)
CREATE TABLE IF NOT EXISTS "Clients" (
    "Id" SERIAL PRIMARY KEY,
    "ClientName" VARCHAR(255) NOT NULL,
    "ContactPerson" VARCHAR(200),
    "Phone" VARCHAR(50),
    "Email" VARCHAR(255),
    "Address" TEXT,
    "CreatedById" INT NULL REFERENCES "Users" ("Id") ON DELETE SET NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 4. Products (Building materials, blocks, bricks)
CREATE TABLE IF NOT EXISTS "Products" (
    "Id" SERIAL PRIMARY KEY,
    "ProductName" VARCHAR(255) NOT NULL,
    "Size" VARCHAR(100),
    "Capacity" VARCHAR(100),
    "DefaultPrice" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 5. Quotes (Quotation headers)
CREATE TABLE IF NOT EXISTS "Quotes" (
    "Id" SERIAL PRIMARY KEY,
    "QuoteNumber" VARCHAR(100) NOT NULL,
    "UserId" INT NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    "CreatedByUserId" INT NULL REFERENCES "Users" ("Id") ON DELETE SET NULL,
    "ClientName" VARCHAR(255) NOT NULL,
    "ClientEmail" VARCHAR(255),
    "ClientPhone" VARCHAR(50),
    "ContactPerson" VARCHAR(200),
    "ContactTitle" VARCHAR(100),
    "IssuerName" VARCHAR(200),
    "IssuerJobTitle" VARCHAR(200),
    "IssuerPrefix" VARCHAR(50),
    "TermsJson" TEXT,
    "ProjectName" VARCHAR(255),
    "Location" VARCHAR(255),
    "TotalAmount" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    "PdfUrl" TEXT,
    "DocxUrl" TEXT,
    "PdfSha256" VARCHAR(64),
    "PdfSizeBytes" BIGINT,
    "StorageFileName" VARCHAR(260),
    "DocxStorageFileName" VARCHAR(260),
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Quotes_QuoteNumber" ON "Quotes" ("QuoteNumber");
CREATE INDEX IF NOT EXISTS "IX_Quotes_UserId" ON "Quotes" ("UserId");
CREATE INDEX IF NOT EXISTS "IX_Quotes_CreatedAt" ON "Quotes" ("CreatedAt" DESC);

-- 6. QuoteItems (Line items per quotation)
CREATE TABLE IF NOT EXISTS "QuoteItems" (
    "Id" SERIAL PRIMARY KEY,
    "QuoteId" INT NOT NULL REFERENCES "Quotes" ("Id") ON DELETE CASCADE,
    "ProductName" VARCHAR(255) NOT NULL,
    "Size" VARCHAR(100),
    "Capacity" VARCHAR(100),
    "Quantity" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    "UnitPrice" NUMERIC(18, 2) NOT NULL DEFAULT 0.00,
    "LineTotal" NUMERIC(18, 2) NOT NULL DEFAULT 0.00
);

CREATE INDEX IF NOT EXISTS "IX_QuoteItems_QuoteId" ON "QuoteItems" ("QuoteId");

-- 7. AuditLogs (Security & compliance trail)
CREATE TABLE IF NOT EXISTS "AuditLogs" (
    "Id" SERIAL PRIMARY KEY,
    "UserId" INT NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    "Action" VARCHAR(100) NOT NULL,
    "EntityId" VARCHAR(255),
    "IpAddress" VARCHAR(50),
    "Timestamp" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS "IX_AuditLogs_UserId" ON "AuditLogs" ("UserId");
CREATE INDEX IF NOT EXISTS "IX_AuditLogs_Timestamp" ON "AuditLogs" ("Timestamp" DESC);

-- 8. TelegramRecipients (Recipients management)
CREATE TABLE IF NOT EXISTS "TelegramRecipients" (
    "Id" SERIAL PRIMARY KEY,
    "DisplayName" VARCHAR(200) NOT NULL,
    "ChatId" VARCHAR(100) NOT NULL,
    "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
    "TotalSent" INT NOT NULL DEFAULT 0,
    "TotalFailed" INT NOT NULL DEFAULT 0,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_TelegramRecipients_ChatId" ON "TelegramRecipients" ("ChatId");

-- 9. QuoteTelegramDeliveries (Transactional Outbox for notifications)
CREATE TABLE IF NOT EXISTS "QuoteTelegramDeliveries" (
    "Id" SERIAL PRIMARY KEY,
    "QuoteId" INT NOT NULL REFERENCES "Quotes" ("Id") ON DELETE CASCADE,
    "QuoteNumber" VARCHAR(100) NOT NULL,
    "ChatId" VARCHAR(100) NOT NULL,
    "Status" VARCHAR(50) NOT NULL DEFAULT 'Pending',
    "RetryCount" INT NOT NULL DEFAULT 0,
    "NextRetryAt" TIMESTAMP WITH TIME ZONE,
    "LastAttemptAt" TIMESTAMP WITH TIME ZONE,
    "ClaimToken" VARCHAR(100),
    "ClaimedAt" TIMESTAMP WITH TIME ZONE,
    "ClaimExpiresAt" TIMESTAMP WITH TIME ZONE,
    "FileSha256" VARCHAR(64),
    "ErrorMessage" TEXT,
    "CreatedAt" TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_QuoteTelegramDeliveries_QuoteId_ChatId" ON "QuoteTelegramDeliveries" ("QuoteId", "ChatId");
CREATE INDEX IF NOT EXISTS "IX_QuoteTelegramDeliveries_Status_NextRetryAt" ON "QuoteTelegramDeliveries" ("Status", "NextRetryAt");

-- Note: No default company products or clients are seeded here.
-- All products and employees are provisioned via ZIN STONE Admin/Owner Dashboard.
