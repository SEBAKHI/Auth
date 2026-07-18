-- Notification Layouts Seed Data
-- Global Email layout migrated verbatim from the legacy SmtpEmailService.BuildHtmlDocument.
-- Liquid placeholders: {{ dir }}, {{ lang }}, {{ content | raw }}, {{ strings.footer | raw }}.
-- The per-language chrome strings live in StringsJson; string values are themselves Liquid
-- templates (the renderer resolves {{ SenderName }} before injecting them).

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

DECLARE @LayoutContent NVARCHAR(MAX) = N'<!DOCTYPE html>
<html dir="{{ dir }}" lang="{{ lang }}">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, ''Segoe UI'', Roboto, ''Helvetica Neue'', Arial, sans-serif; margin: 0; padding: 0; background-color: #f5f5f5; direction: {{ dir }}; text-align: {% if dir == "rtl" %}right{% else %}left{% endif %}; }
        .container { max-width: 600px; margin: 0 auto; padding: 40px 20px; }
        .card { background: #ffffff; border-radius: 8px; padding: 40px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { text-align: center; margin-bottom: 30px; }
        .header h1 { color: #1a1a1a; font-size: 24px; margin: 0; }
        .code-container { text-align: center; margin: 30px 0; }
        .otp-code { display: inline-block; font-size: 36px; font-weight: bold; letter-spacing: 8px; color: #2563eb; padding: 20px 30px; background: #f3f4f6; border-radius: 8px; font-family: ''Courier New'', monospace; direction: ltr; }
        .token-code { display: inline-block; font-size: 16px; font-weight: bold; color: #2563eb; padding: 12px 20px; background: #f3f4f6; border-radius: 8px; font-family: ''Courier New'', monospace; direction: ltr; word-break: break-all; }
        .button-container { text-align: center; margin: 30px 0; }
        .button { display: inline-block; background: #2563eb; color: #ffffff !important; font-size: 16px; font-weight: bold; text-decoration: none; padding: 14px 28px; border-radius: 6px; }
        .link-fallback { color: #6b7280; font-size: 13px; word-break: break-all; }
        .message { color: #4b5563; font-size: 16px; line-height: 1.6; }
        .warning { color: #dc2626; font-size: 14px; margin-top: 30px; padding: 15px; background: #fef2f2; border-radius: 6px; }
        .footer { text-align: center; margin-top: 30px; color: #9ca3af; font-size: 14px; }
    </style>
</head>
<body>
    <div class="container">
        <div class="card">
{{ content | raw }}
        </div>
        <div class="footer">
            <p>{{ strings.footer | raw }}</p>
        </div>
    </div>
</body>
</html>';

DECLARE @LayoutStrings NVARCHAR(MAX) = N'{
"en": {"footer": "This is an automated message from {{ SenderName }}. Please do not reply to this email."},
"ar": {"footer": "هذه رسالة تلقائية من {{ SenderName }}. يرجى عدم الرد على هذا البريد الإلكتروني."},
"tr": {"footer": "Bu, {{ SenderName }} tarafından gönderilen otomatik bir mesajdır. Lütfen bu e-postayı yanıtlamayın."},
"fr": {"footer": "Ceci est un message automatique de {{ SenderName }}. Veuillez ne pas répondre à cet e-mail."},
"zh": {"footer": "这是来自{{ SenderName }}的自动消息，请勿回复此邮件。"},
"ur": {"footer": "یہ {{ SenderName }} کی طرف سے ایک خودکار پیغام ہے۔ براہ کرم اس ای میل کا جواب نہ دیں۔"},
"fa": {"footer": "این یک پیام خودکار از {{ SenderName }} است. لطفاً به این ایمیل پاسخ ندهید."}
}';

IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationLayouts] WHERE [Id] = '41000000-0000-0000-0000-000000000001')
BEGIN
    INSERT INTO [dbo].[NotificationLayouts]
        ([Id], [ApplicationId], [Channel], [Name], [DraftContent], [DraftStringsJson], [PublishedContent], [PublishedStringsJson], [PublishedAt], [PublishedBy], [CreatedAt], [CreatedBy])
    VALUES
        ('41000000-0000-0000-0000-000000000001', NULL, 1, N'Default Email Layout',
         @LayoutContent, @LayoutStrings, @LayoutContent, @LayoutStrings,
         GETUTCDATE(), @SystemUserId, GETUTCDATE(), @SystemUserId);
    PRINT 'Created default global email layout (published)';
END
ELSE
BEGIN
    PRINT 'Default email layout already exists';
END
GO
