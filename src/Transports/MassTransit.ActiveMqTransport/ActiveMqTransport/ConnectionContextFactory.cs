namespace MassTransit.ActiveMqTransport
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Agents;
    using Apache.NMS;
    using Configuration;
    using Internals;
    using Transports;


    public class ConnectionContextFactory :
        IPipeContextFactory<ConnectionContext>
    {
        readonly IActiveMqHostConfiguration _hostConfiguration;

        public ConnectionContextFactory(IActiveMqHostConfiguration hostConfiguration)
        {
            _hostConfiguration = hostConfiguration;
        }

        IPipeContextAgent<ConnectionContext> IPipeContextFactory<ConnectionContext>.CreateContext(ISupervisor supervisor)
        {
            Task<ConnectionContext> context = Task.Run(() => CreateConnection(supervisor), supervisor.Stopped);

            IPipeContextAgent<ConnectionContext> contextHandle = supervisor.AddContext(context);

            void HandleException(Exception exception)
            {
                TransportLogMessages.ExceptionListenerHandled(exception.ToString());
                if (exception.ToString().Contains("not correlate acknowledgment"))
                    return;
                else
                    contextHandle.Stop($"Connection Exception: {exception}");
            }

            context.ContinueWith(task =>
            {
                task.Result.Context.ExceptionListener += HandleException;
                contextHandle.Completed.ContinueWith(_ =>
                {
                    task.Result.Context.ExceptionListener -= HandleException;
                });
            }, TaskContinuationOptions.OnlyOnRanToCompletion);

            return contextHandle;
        }

        IActivePipeContextAgent<ConnectionContext> IPipeContextFactory<ConnectionContext>.CreateActiveContext(ISupervisor supervisor,
            PipeContextHandle<ConnectionContext> context, CancellationToken cancellationToken)
        {
            return supervisor.AddActiveContext(context, CreateSharedConnection(context.Context, cancellationToken));
        }

        static async Task<ConnectionContext> CreateSharedConnection(Task<ConnectionContext> context, CancellationToken cancellationToken)
        {
            return context.IsCompletedSuccessfully()
                ? new SharedConnectionContext(context.Result, cancellationToken)
                : new SharedConnectionContext(await context.OrCanceled(cancellationToken).ConfigureAwait(false), cancellationToken);
        }

        async Task<ConnectionContext> CreateConnection(ISupervisor supervisor)
        {
            var description = _hostConfiguration.Settings.ToDescription();

            if (supervisor.Stopping.IsCancellationRequested)
                throw new ActiveMqConnectionException($"The connection is stopping and cannot be used: {description}");

            INMSContext context = null;
            try
            {
                TransportLogMessages.ConnectHost(description);

                context = _hostConfiguration.Settings.CreateContext();

                await context.StartAsync();
                context.ConnectionInterruptedListener += () => LogContext.Info?.Log("Connection interrupted: {Host} (client-id: {ClientId})", description, context.ClientId);
                context.ConnectionResumedListener += () => LogContext.Info?.Log("Connection resumed: {Host} (client-id: {ClientId})", description, context.ClientId);
                LogContext.Info?.Log("Connected: {Host} (client-id: {ClientId})", description,
                    context.ClientId);

                return new ActiveMqConnectionContext(context, _hostConfiguration, supervisor.Stopped);
            }
            catch (OperationCanceledException)
            {
                context?.Dispose();
                throw;
            }
            catch (NMSConnectionException ex)
            {
                context?.Dispose();
                LogContext.Warning?.Log(ex, "Connection Failed: {InputAddress}", _hostConfiguration.HostAddress);
                throw new ActiveMqConnectionException("Connection exception: " + description, ex);
            }
            catch (Exception ex)
            {
                context?.Dispose();
                LogContext.Warning?.Log(ex, "Connection Failed: {InputAddress}", _hostConfiguration.HostAddress);
                throw new ActiveMqConnectionException("Create Connection Faulted: " + description, ex);
            }
        }
    }
}
