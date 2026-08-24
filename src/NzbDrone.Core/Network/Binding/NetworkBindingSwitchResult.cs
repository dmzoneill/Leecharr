namespace NzbDrone.Core.Network.Binding;

public class NetworkBindingSwitchResult
{
    public bool Success { get; set; }
    public string PreviousProvider { get; set; }
    public string ActiveProvider { get; set; }
    public string Message { get; set; }
    public string Error { get; set; }
}
