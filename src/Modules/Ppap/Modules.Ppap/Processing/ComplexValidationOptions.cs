namespace DES.Modules.Ppap.Processing;

public sealed class ComplexValidationOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "fsh.events";
    public string RequestQueueName { get; set; } = "ppap.complex-validation.requests";
    public string ResultQueueName { get; set; } = "ppap.complex-validation.results";
    public bool UseSsl { get; set; }
    public ushort PrefetchCount { get; set; } = 8;
}
