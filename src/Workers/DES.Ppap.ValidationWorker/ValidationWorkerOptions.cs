using System.ComponentModel.DataAnnotations;

namespace DES.Ppap.ValidationWorker;

internal sealed class ValidationWorkerOptions
{
    public bool Enabled { get; set; }
    [Required] public string Host { get; set; } = "localhost";
    [Range(1, 65535)] public int Port { get; set; } = 5672;
    [Required] public string UserName { get; set; } = "guest";
    [Required] public string Password { get; set; } = "guest";
    [Required] public string VirtualHost { get; set; } = "/";
    [Required] public string ExchangeName { get; set; } = "fsh.events";
    [Required] public string RequestQueueName { get; set; } = "ppap.complex-validation.requests";
    public bool UseSsl { get; set; }
    [Range(1, 100)] public ushort PrefetchCount { get; set; } = 4;
}
