﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClamAV.Net.Client.Results;
using ClamAV.Net.Commands;
using ClamAV.Net.Commands.Base;
using ClamAV.Net.Connection;
using ClamAV.Net.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClamAV.Net.Client
{
    /// <summary>
    /// ClamAV client
    /// </summary>
    public class ClamAvClient : IClamAvClient
    {
        private readonly IConnectionFactory mConnectionFactory;
        private readonly ILogger<ClamAvClient> mLogger;
        private IConnection mConnection;
        private readonly SemaphoreSlim mSemaphoreSlim = new SemaphoreSlim(1,1);

        /// <summary>
        /// Create ClamAV client
        /// </summary>
        /// <param name="connectionUri">Connection Uri</param>
        /// <param name="loggerFactory">Optional logger factory</param>
        /// <returns>IClamAvClient ClamAV client</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public static IClamAvClient Create(Uri connectionUri, ILoggerFactory loggerFactory = null)
        {
            ILoggerFactory tmpLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

            return new ClamAvClient(new ConnectionFactory(connectionUri, tmpLoggerFactory),
                tmpLoggerFactory.CreateLogger<ClamAvClient>());
        }

        internal ClamAvClient(IConnectionFactory connectionFactory, ILogger<ClamAvClient> logger)
        {
            mConnectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            mLogger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private async Task<TResponse> SendCommand<TResponse>(ICommand<TResponse> command,
            CancellationToken cancellationToken)
        {
            try
            {
                await mSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (!(mConnection?.IsConnected ?? false))
                {
                    await InitConnection(cancellationToken).ConfigureAwait(false);

                    //open new session
                    await mConnection.SendCommandAsync(new IdSessionCommand(), cancellationToken).ConfigureAwait(false);
                }

                return await mConnection.SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            }
            catch (ClamAvException e)
            {
                mLogger.LogError(e, "ClamAV error occured");
                throw;
            }
            catch (Exception e)
            {
                mLogger.LogError(e, "General error occured");
                throw new ClamAvException("ClamAV client error occured", e);
            }
            finally
            {
                mSemaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Ping ClamAV server.
        /// Run PING command on the ClamAV server
        /// </summary>
        /// <param name="cancellationToken">Cancellation token used to operation cancel</param>
        /// <exception cref="ClamAvException">Thrown when command failed</exception>
        public Task<VersionResult> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            mLogger.LogTrace($"Send {nameof(VersionCommand)} to the server");

            return SendCommand(new VersionCommand(), cancellationToken);
        }

        /// <summary>
        /// Ping ClamAV server.
        /// Run PING command on the ClamAV server
        /// </summary>
        /// <param name="cancellationToken">Cancellation token used to operation cancel</param>
        /// <exception cref="ClamAvException">Thrown when command failed</exception>
        public Task PingAsync(CancellationToken cancellationToken = default)
        {
            mLogger.LogTrace($"Send {nameof(PingCommand)} to the server");

            return SendCommand(new PingCommand(), cancellationToken);
        }

        /// <summary>
        /// Scan a stream of data. The stream is sent to ClamAV in chunks.
        /// Run INSTREAM command on the ClamAV server
        /// </summary>
        /// <param name="dataStream">Data stream to scan. The stream should support read operation</param>
        /// /// <param name="cancellationToken">Cancellation token used to operation cancel</param>
        /// <returns>ScanResult</returns>
        /// <exception cref="ClamAvException">Thrown when command failed</exception>
        public Task<ScanResult> ScanDataAsync(Stream dataStream, CancellationToken cancellationToken = default)
        {
            mLogger.LogTrace($"Send {nameof(InStreamCommand)} to the server");

            return SendCommand(new InStreamCommand(dataStream), cancellationToken);
        }

        /// <summary>
        /// Scan a stream of data. The stream is sent to ClamAV in chunks.
        /// Run SCAN command on the ClamAV server
        /// </summary>
        /// <param name="remotePath">Path on the ClamAV server</param>
        /// /// <param name="cancellationToken">Cancellation token used to operation cancel</param>
        /// <returns>ScanResult</returns>
        /// <exception cref="ClamAvException">Thrown when command failed</exception>
        public Task<ScanResult> ScanRemotePathAsync(string remotePath, CancellationToken cancellationToken = default)
        {
            mLogger.LogTrace($"Send {nameof(ScanCommand)} to the server");

            return SendCommand(new ScanCommand(remotePath), cancellationToken);
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private async Task InitConnection(CancellationToken cancellationToken)
        {
            mConnection?.Dispose();

            mConnection = await mConnectionFactory.CreateAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            if (mConnection?.IsConnected ?? false)
                mConnection.SendCommandAsync(new EndCommand(), CancellationToken.None).Wait();

            mConnection?.Dispose();
        }

        /// <summary>
        /// </summary>
        ~ClamAvClient()
        {
            Dispose(false);
        }
    }
}