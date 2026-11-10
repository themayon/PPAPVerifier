using System.Text;
using System.Text.Json;
using DES.Modules.Ppap.Contracts.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DES.Ppap.ValidationWorker;

internal sealed class ComplexValidationWorker(
    IOptions<ValidationWorkerOptions> options,
    ComplexValidationExecutorRegistry registry,
    ILogger<ComplexValidationWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ValidationWorkerOptions _options = options.Value;
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogWarning("Complex validation worker is disabled");
            return;
        }

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
        _connection = await factory.CreateConnectionAsync(stoppingToken).ConfigureAwait(false);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken).ConfigureAwait(false);
        await _channel.ExchangeDeclareAsync(_options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: stoppingToken).ConfigureAwait(false);
        await _channel.QueueDeclareAsync(_options.RequestQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken).ConfigureAwait(false);
        string routingKey = typeof(ComplexValidationRequestedIntegrationEvent).FullName!;
        await _channel.QueueBindAsync(_options.RequestQueueName, _options.ExchangeName, routingKey, cancellationToken: stoppingToken).ConfigureAwait(false);
        await _channel.BasicQosAsync(0, _options.PrefetchCount, global: false, cancellationToken: stoppingToken).ConfigureAwait(false);

        AsyncEventingBasicConsumer consumer = new(_channel);
        consumer.ReceivedAsync += HandleDeliveryAsync;
        await _channel.BasicConsumeAsync(_options.RequestQueueName, autoAck: false, consumer, cancellationToken: stoppingToken).ConfigureAwait(false);
        if (logger.IsEnabled(LogLevel.Information)) logger.LogInformation("Complex validation worker is consuming queue {QueueName}", _options.RequestQueueName);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
    }

    private async Task HandleDeliveryAsync(object sender, BasicDeliverEventArgs args)
    {
        if (_channel is null) return;
        Guid requestId = Guid.Empty;
        try
        {
            ComplexValidationRequestedIntegrationEvent request = JsonSerializer.Deserialize<ComplexValidationRequestedIntegrationEvent>(args.Body.Span, JsonOptions)
                ?? throw new JsonException("Validation request payload is empty.");
            requestId = request.Id;
            ComplexValidationExecutionResult result;
            try
            {
                IComplexValidationExecutor executor = registry.Resolve(request.ExecutorKey);
                result = await executor.ExecuteAsync(request, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or JsonException)
            {
                logger.LogWarning(exception, "Validation request {RequestId} failed in executor {ExecutorKey}", request.Id, request.ExecutorKey);
                result = new ComplexValidationExecutionResult("Error", "The complex validator could not evaluate this rule.", "1.0", 0, []);
            }

            ComplexValidationCompletedIntegrationEvent completed = new(
                Guid.CreateVersion7(),
                DateTime.UtcNow,
                request.TenantId,
                request.CorrelationId,
                "PpapValidationWorker",
                request.Id,
                request.SubmissionId,
                request.ValidationRunId,
                request.RuleVersionId,
                request.StableRuleCode,
                request.RuleName,
                request.ExecutorKey,
                result.ExecutorVersion,
                request.InputFingerprint,
                result.Status,
                result.Message,
                result.DurationMilliseconds,
                result.Evidence);
            await PublishCompletedAsync(completed, args.CancellationToken).ConfigureAwait(false);
            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, args.CancellationToken).ConfigureAwait(false);
            if (logger.IsEnabled(LogLevel.Information)) logger.LogInformation("Complex validation request {RequestId} completed with status {Status}", request.Id, result.Status);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Discarding malformed complex validation message {MessageId}", args.BasicProperties.MessageId);
            await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Complex validation delivery failed for request {RequestId}", requestId);
            await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, args.CancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishCompletedAsync(ComplexValidationCompletedIntegrationEvent completed, CancellationToken cancellationToken)
    {
        if (_channel is null) throw new InvalidOperationException("RabbitMQ channel is unavailable.");
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(completed, JsonOptions);
        BasicProperties properties = new()
        {
            MessageId = completed.Id.ToString(),
            CorrelationId = completed.CorrelationId,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(completed.OccurredOnUtc).ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                ["event-type"] = typeof(ComplexValidationCompletedIntegrationEvent).AssemblyQualifiedName,
                ["tenant-id"] = completed.TenantId,
                ["source"] = completed.Source
            }
        };
        await _channel.BasicPublishAsync(
            _options.ExchangeName,
            typeof(ComplexValidationCompletedIntegrationEvent).FullName!,
            mandatory: true,
            properties,
            body,
            cancellationToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken).ConfigureAwait(false);
            _channel.Dispose();
        }
        if (_connection is not null)
        {
            await _connection.CloseAsync(cancellationToken).ConfigureAwait(false);
            _connection.Dispose();
        }
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
