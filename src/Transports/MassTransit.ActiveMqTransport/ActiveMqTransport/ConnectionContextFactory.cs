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

            void HandleConnectionException(Exception exception)
            {
                contextHandle.Stop($"Connection Exception: {exception}");
            }

            context.ContinueWith(task =>
            {
                task.Result.Context.ExceptionListener += HandleConnectionException;

                contextHandle.Completed.ContinueWith(_ => task.Result.Context.ExceptionListener -= HandleConnectionException);
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

                LogContext.Debug?.Log("Connected: {Host} (client-id: {ClientId})", description,
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
