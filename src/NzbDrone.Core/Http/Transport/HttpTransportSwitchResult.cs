namespace NzbDrone.Core.Http.Transport;

public class HttpTransportSwitchResult
{
    public bool Success { get; set; }
    public string PreviousProvider { get; set; }
    public string ActiveProvider { get; set; }
    public string Message { get; set; }
    public string Error { get; set; }
}
