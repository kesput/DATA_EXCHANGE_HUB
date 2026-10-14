namespace DATA_EXCHANGE_HUB.Services;

public sealed class SendSmsOptions
{
    public const string SectionName = "SendSms";

    public string ConnectionStringName { get; set; } = "STAT3";

    public string ProcedureName { get; set; } = "WM0.SNTXDB.dbo.SEND_SMS";

    public int CommandTimeoutSeconds { get; set; } = 30;
}

