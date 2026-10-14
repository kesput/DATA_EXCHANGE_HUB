namespace DATA_EXCHANGE_HUB.Services;

public sealed class SendMailOptions
{
    public const string SectionName = "SendMail";

    public string ConnectionStringName { get; set; } = "DataExchangeSql";

    public string LogTableName { get; set; } = "BLS_LOGIT_STAT.dbo.HUB_SEND_MAIL_LOG";

    public string LocalArchivePath { get; set; } = @"Q:\DATA\SEND_MAIL\HUB";

    public string IisArchivePath { get; set; } = @"Q:\QLIKDATA\DATA\SEND_MAIL\HUB";

    public string LocalSignaturePath { get; set; } = @"Q:\DATA\Q_LOGO\REPORT_SIGNATURE.html";

    public string IisSignaturePath { get; set; } = @"Q:\QLIKDATA\DATA\Q_LOGO\REPORT_SIGNATURE.html";

    public string SmtpServer { get; set; } = "192.168.2.40";

    public int SmtpPort { get; set; } = 25;

    public bool EnableSsl { get; set; }

    public string SmtpUserName { get; set; } = string.Empty;

    public string SmtpPassword { get; set; } = string.Empty;

    public int SmtpTimeoutSeconds { get; set; } = 90;

    public string FromDisplayName { get; set; } = "Auto Mailer";

    public string FromEmail { get; set; } = "noreply@bls.lt";

    public List<SendMailSenderProfileOptions> SenderProfiles { get; set; } =
    [
        new()
        {
            Key = "bls",
            DisplayName = "Auto Mailer",
            Email = "statistics@bls.lt",
            SignatureKind = "bls"
        },
        new()
        {
            Key = "sanitex",
            DisplayName = "Statistics",
            Email = "statistics@sanitex.eu",
            SignatureKind = "sanitex"
        }
    ];

    public string TestAccessRequestRecipient { get; set; } = "kestutis.p@bls.lt";

    public string SecurityAlertRecipient { get; set; } = "kestutis.p@bls.lt";

    public string PlatformUrl { get; set; } = "https://blt-srvqlk1-prd.sanitex.org/";

    public int RecentLimit { get; set; } = 30;
}

public sealed class SendMailSenderProfileOptions
{
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string SignatureKind { get; set; } = "bls";
}
