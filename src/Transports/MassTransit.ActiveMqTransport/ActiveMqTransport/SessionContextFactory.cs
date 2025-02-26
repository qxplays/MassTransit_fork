﻿namespace MassTransit.ActiveMqTransport
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Agents;
    using Internals;
    using Transports;


    public class SessionContextFactory :
        IPipeContextFactory<SessionContext>
    {
        readonly IConnectionContextSupervisor _connectionContextSupervisor;

        public SessionContextFactory(IConnectionContextSupervisor connectionContextSupervisor)
        {
            _connectionContextSupervisor = connectionContextSupervisor;
        }

        public IPipeContextAgent<SessionContext> CreateContext(ISupervisor supervisor)
        {
            IAsyncPipeContextAgent<SessionContext> asyncContext = supervisor.AddAsyncContext<SessionContext>();

            CreateSession(asyncContext, supervisor.Stopped);

            return asyncContext;
        }

        public IActivePipeContextAgent<SessionContext> CreateActiveContext(ISupervisor supervisor,
            PipeContextHandle<SessionContext> context, CancellationToken cancellationToken)
        {
            return supervisor.AddActiveContext(context, CreateSharedSession(context.Context, cancellationToken));
        }

        static async Task<SessionContext> CreateSharedSession(Task<SessionContext> context, CancellationToken cancellationToken)
        {
            return context.IsCompletedSuccessfully()
                ? new ScopeSessionContext(context.Result, cancellationToken)
                : new ScopeSessionContext(await context.OrCanceled(cancellationToken).ConfigureAwait(false), cancellationToken);
        }

        void CreateSession(IAsyncPipeContextAgent<SessionContext> asyncContext, CancellationToken cancellationToken)
        {
            async Task<SessionContext> CreateSessionContext(ConnectionContext connectionContext, CancellationToken createCancellationToken)
            {
                void HandleConnectionException(Exception exception)
                {
                    TransportLogMessages.ExceptionListenerHandled(exception.ToString());
                    if (exception.ToString().Contains("not correlate acknowledgment"))
                        return;
                    // ReSharper disable once MethodSupportsCancellation
                    asyncContext.Stop($"Connection Exception: {exception}");
                }

                connectionContext.Context.ExceptionListener += HandleConnectionException;

                #pragma warning disable 4014
                // ReSharper disable once MethodSupportsCancellation
                asyncContext.Completed.ContinueWith(_ => connectionContext.Context.ExceptionListener -= HandleConnectionException,
                    TaskContinuationOptions.ExecuteSynchronously);
                #pragma warning restore 4014

                return new ActiveMqSessionContext(connectionContext, createCancellationToken);
            }

            #pragma warning disable CS4014
            _connectionContextSupervisor.CreateAgent(asyncContext, CreateSessionContext, cancellationToken);
            #pragma warning restore CS4014
        }
    }
}
