using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DES.Framework.Eventing.Abstractions;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Processing;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DES.Modules.Ppap.IntegrationEventHandlers;

[SuppressMessage("Naming", "CA1711", Justification = "The required integration-event handler name describes its role.")]
public sealed class ComplexValidationRequestedIntegrationEventHandler(
    IOptions<ComplexValidationOptions> options) : IIntegrationEventHandler<ComplexValidationRequestedIntegrationEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ComplexValidationOptions _options = options.Value;

    public async Task HandleAsync(
        ComplexValidationRequestedIntegrationEvent @event,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (!_options.Enabled) throw new InvalidOperationException("Complex validation messaging is disabled.");
        ConnectionFactory factory = new()
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            Ssl = new SslOption { Enabled = _options.UseSsl },
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };
        IConnection? connection = null;
        IChannel? channel = null;
        try
        {
            connection = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);
            channel = await connection.CreateChannelAsync(cancellationToken: ct).ConfigureAwait(false);
            await channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct).ConfigureAwait(false);
            await channel.QueueDeclareAsync(_options.RequestQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct).ConfigureAwait(false);
            string routingKey = typeof(ComplexValidationRequestedIntegrationEvent).FullName!;
            await channel.QueueBindAsync(_options.RequestQueueName, _options.ExchangeName, routingKey, cancellationToken: ct).ConfigureAwait(false);
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(@event, JsonOptions);
            BasicProperties properties = new()
            {
                MessageId = @event.Id.ToString(),
                CorrelationId = @event.CorrelationId,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(new DateTimeOffset(@event.OccurredOnUtc).ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["event-type"] = typeof(ComplexValidationRequestedIntegrationEvent).AssemblyQualifiedName,
                    ["tenant-id"] = @event.TenantId,
                    ["source"] = @event.Source
                }
            };
            await channel.BasicPublishAsync(
                _options.ExchangeName,
                routingKey,
                mandatory: true,
                properties,
                body,
                ct).ConfigureAwait(false);
        }
        finally
        {
            if (channel is not null)
            {
                await channel.CloseAsync(ct).ConfigureAwait(false);
                channel.Dispose();
            }
            if (connection is not null)
            {
                await connection.CloseAsync(ct).ConfigureAwait(false);
                connection.Dispose();
            }
        }
    }
}
