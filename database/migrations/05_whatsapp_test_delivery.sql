-- Apply only after a verified backup. No background sender is enabled by this migration.
SET XACT_ABORT ON;
IF DB_NAME() <> 'db76143' THROW 50000, 'Unapproved ZIN STONE database', 1;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.WhatsAppDeliveryAttempts') IS NULL
BEGIN
 CREATE TABLE dbo.WhatsAppDeliveryAttempts (
  Id nvarchar(64) NOT NULL PRIMARY KEY,
  QuoteNumber nvarchar(100) NOT NULL,
  RecipientPhone nvarchar(20) NOT NULL,
  FileSha256 nvarchar(64) NOT NULL,
  Status nvarchar(30) NOT NULL,
  MediaId nvarchar(200) NULL,
  MessageId nvarchar(200) NULL,
  UploadAttempts int NOT NULL,
  SendAttempts int NOT NULL,
  HttpStatus int NULL,
  CreatedAt datetime2 NOT NULL,
  UpdatedAt datetime2 NULL
 );
 CREATE INDEX IX_WhatsAppDeliveryAttempts_MessageId ON dbo.WhatsAppDeliveryAttempts(MessageId);
END;
COMMIT;
-- Reversal: leave the unused table and its audit records intact, disable WhatsApp flags.
