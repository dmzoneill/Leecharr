namespace NzbDrone.Core.HealthCheck;

public interface IHealthCheck
{
    HealthCheckResult Check();
}

public enum HealthCheckResultType
{
    Ok,
    Notice,
    Warning,
    Error
}

public class HealthCheckResult
{
    public HealthCheckResultType Type { get; set; }
    public string Source { get; set; }
    public string Message { get; set; }

    public static HealthCheckResult Ok(string source)
    {
        return new HealthCheckResult { Type = HealthCheckResultType.Ok, Source = source };
    }

    public static HealthCheckResult Notice(string source, string message)
    {
        return new HealthCheckResult { Type = HealthCheckResultType.Notice, Source = source, Message = message };
    }

    public static HealthCheckResult Warning(string source, string message)
    {
        return new HealthCheckResult { Type = HealthCheckResultType.Warning, Source = source, Message = message };
    }

    public static HealthCheckResult Error(string source, string message)
    {
        return new HealthCheckResult { Type = HealthCheckResultType.Error, Source = source, Message = message };
    }
}
