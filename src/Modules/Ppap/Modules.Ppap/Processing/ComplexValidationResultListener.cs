using System.Text.Json;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Ppap.Contracts.Events;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DES.Modules.Ppap.Processing;

internal sealed class ComplexValidationResultListener(
    IOptions<ComplexValidationOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<ComplexValidationResultListener> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ComplexValidationOptions _options = options.Value;
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
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
        await _channel.QueueDeclareAsync(_options.ResultQueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken).ConfigureAwait(false);
        await _channel.QueueBindAsync(_options.ResultQueueName, _options.ExchangeName, typeof(ComplexValidationCompletedIntegrationEvent).FullName!, cancellationToken: stoppingToken).ConfigureAwait(false);
        await _channel.BasicQosAsync(0, _options.PrefetchCount, global: false, cancellationToken: stoppingToken).ConfigureAwait(false);
        AsyncEventingBasicConsumer consumer = new(_channel);
        consumer.ReceivedAsync += HandleAsync;
        await _channel.BasicConsumeAsync(_options.ResultQueueName, autoAck: false, consumer, cancellationToken: stoppingToken).ConfigureAwait(false);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
    }

    private async Task HandleAsync(object sender, BasicDeliverEventArgs args)
    {
        if (_channel is null) return;
        try
        {
            ComplexValidationCompletedIntegrationEvent result = JsonSerializer.Deserialize<ComplexValidationCompletedIntegrationEvent>(args.Body.Span, JsonOptions)
                ?? throw new JsonException("Validation completion payload is empty.");
            using IServiceScope scope = scopeFactory.CreateScope();
            IMultiTenantStore<AppTenantInfo> tenantStore = scope.ServiceProvider.GetRequiredService<IMultiTenantStore<AppTenantInfo>>();
            AppTenantInfo? tenant = await tenantStore.GetAsync(result.TenantId).ConfigureAwait(false);
            if (tenant is null) throw new InvalidOperationException($"Tenant {result.TenantId} was not found for complex validation result {result.Id}.");
            IMultiTenantContextSetter tenantContextSetter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
            tenantContextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(tenant);
            ComplexValidationResultProcessor processor = scope.ServiceProvider.GetRequiredService<ComplexValidationResultProcessor>();
            await processor.ProcessAsync(result, args.CancellationToken).ConfigureAwait(false);
            await _channel.BasicAckAsync(args.DeliveryTag, multiple: false, args.CancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Discarding malformed complex validation result {MessageId}", args.BasicProperties.MessageId);
            await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to persist complex validation result {MessageId}", args.BasicProperties.MessageId);
            await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, args.CancellationToken).ConfigureAwait(false);
        }
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
