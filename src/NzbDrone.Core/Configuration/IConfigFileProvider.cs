namespace NzbDrone.Core.Configuration;

public interface IConfigFileProvider
{
    string BindAddress { get; }
    int Port { get; }
    bool EnableSsl { get; }
    string ApiKey { get; }
    bool AuthenticationEnabled { get; }
    string LogLevel { get; }
    string UrlBase { get; }
    string PostgresHost { get; }
    int PostgresPort { get; }
    string PostgresMainDb { get; }
    string PostgresUser { get; }
    string PostgresPassword { get; }
}
