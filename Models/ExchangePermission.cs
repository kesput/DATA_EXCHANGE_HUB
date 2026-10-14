namespace DATA_EXCHANGE_HUB.Models;

[Flags]
public enum ExchangePermission
{
    None = 0,
    Receive = 1,
    Upload = 2,
    UploadAndReceive = Receive | Upload
}
