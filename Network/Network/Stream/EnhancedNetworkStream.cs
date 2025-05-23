﻿using Network.Architecture;
using Network.Architecture.Interfaces;
using Network.Architecture.Interfaces.Protocol;
using Network.Stream.Configuration;
using System.Net.Sockets;
using Helpers.Extension;
using Helpers.Utility.Lifecycle;
using System;

namespace Network.Stream;

/// <summary>
/// Represents an enhanced network stream which supports sending and receiving messages using a defined protocol.
/// </summary>
/// <typeparam name="TSendMessage">The type of message that can be sent.</typeparam>
/// <typeparam name="TReceiveMessage">The type of message that can be received.</typeparam>
public class EnhancedNetworkStream<TSendMessage, TReceiveMessage> : LifecycleComponent, IMessageSender<TSendMessage>
    where TSendMessage : IMessage
    where TReceiveMessage : IMessage
{
    /// <summary>
    /// The underlying network stream.
    /// </summary>
    private NetworkStream stream;

    /// <summary>
    /// The configuration of the network stream.
    /// </summary>
    private EnhancedNetworkStreamConfiguration<TSendMessage, TReceiveMessage> configuration;

    /// <summary>
    /// The cancellation token source used for the lifecycle operations.
    /// </summary>
    private CancellationTokenSource? cancellationTokenSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnhancedNetworkStream{TSendMessage, TReceiveMessage}"/> class.
    /// </summary>
    /// <param name="networkStream">The network stream to communicate over.</param>
    /// <param name="configuration">The configuration for the stream and message protocol.</param>
    public EnhancedNetworkStream(NetworkStream networkStream, EnhancedNetworkStreamConfiguration<TSendMessage, TReceiveMessage> configuration)
    {
        this.stream = networkStream;
        this.configuration = configuration;
        this.State = LifecycleState.Initialized;
    }

    /// <summary>
    /// Is raised when a message is received from the network stream.
    /// </summary>
    public event EventHandler<NetworkStreamDataReceivedEventArgs>? DataReceived;

    /// <summary>
    /// Starts the network stream and begins polling for incoming messages in the background.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the stream has already been started.</exception>
    public override void Start()
    {
        if (this.state == LifecycleState.Started)
        {
            throw new InvalidOperationException("Network stream was already started.");
        }

        this.State = LifecycleState.Started;
        this.cancellationTokenSource = new CancellationTokenSource();
        Task _ = Task.Run(() => this.PollForDataAsync(this.cancellationTokenSource.Token));
    }

    /// <summary>
    /// Stops the network stream and cancels ongoing background polling for messages.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the stream is not currently running.</exception>
    public override void Stop()
    {
        if (this.state == LifecycleState.Stopped)
        {
            return;
        }

        if (this.state != LifecycleState.Started)
        {
            throw new InvalidOperationException("Network stream is not running.");
        }

        this.cancellationTokenSource?.Cancel();
        this.cancellationTokenSource = null;
        this.State = LifecycleState.Stopped;
    }

    /// <inheritdoc />
    public void Send(TSendMessage message)
    {
        try
        {
            ReadOnlySpan<byte> encodedMessage = this.configuration.MessageProtocol.Encode(message).Span;
            this.stream.Write(encodedMessage);
        }
        catch
        {
            this.Stop();
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(TSendMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            ReadOnlyMemory<byte> encodedMessage = this.configuration.MessageProtocol.Encode(message);
            await stream.WriteAsync(encodedMessage, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch
        {
            this.Stop();
        }
    }

    /// <inheritdoc />
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        try
        {
            await stream.WriteAsync(data, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch
        {
            this.Stop();
        }
    }

    /// <summary>
    /// Like <see cref="Stop"/> - Handles unexpected failures by stopping the network stream and canceling ongoing operations.
    /// </summary>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the stream is not in a running state when attempting to handle a failure.
    /// </exception>
    /// <remarks>
    /// This method cancels the polling task, resets the internal state to <see cref="LifecycleState.Stopped"/>,
    /// and triggers the <see cref="LifecycleComponent.Stopped"/> event.
    /// </remarks>
    protected override void Fail(Exception exception)
    {
        if (this.state == LifecycleState.Stopped)
        {
            return;
        }

        if (this.state != LifecycleState.Started)
        {
            throw new InvalidOperationException("Network stream is not running.");
        }

        this.cancellationTokenSource?.Cancel();
        this.cancellationTokenSource = null;

        // not using setter to avoid sending 2 events
        this.state = LifecycleState.Stopped;
        this.FireOnStopped(exception);
    }

    /// <summary>
    /// Fíres the <see cref="DataReceived"/> event.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected virtual void FireOnDataReceived(NetworkStreamDataReceivedEventArgs e)
    {
        this.DataReceived?.Invoke(this, e);
    }

    /// <summary>
    /// Extracts all full messages currently available in the buffer.
    /// </summary>
    /// <param name="dataBuffer">The buffer containing potentially multiple message and/or leftover bytes.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A list of fully extracted message byte blocks.</returns>
    private async Task<List<ReadOnlyMemory<byte>>> ExtractAllMessagesAsync(MemoryStream dataBuffer, CancellationToken cancellationToken = default)
    {
        List<ReadOnlyMemory<byte>> messages = [];
        byte[] internalBuffer = dataBuffer.GetBuffer();
        int length = dataBuffer.Length.ConvertToInt();
        int offset = 0;

        while (offset < length)
        {
            ReadOnlyMemory<byte> currentData = internalBuffer.AsMemory(offset, length - offset);

            if (!this.configuration.MessageProtocol.TryGetMessageSize(currentData, out int messageSize))
            {
                // Not enough data to determine message size
                break;
            }

            if (currentData.Length < messageSize)
            {
                // Not enough data for message
                break;
            }

            ReadOnlyMemory<byte> fullMessage = currentData[..messageSize];
            messages.Add(fullMessage);
            offset += messageSize;
        }

        // Write remaining data into a new memory stream
        int remaining = length - offset;
        dataBuffer.SetLength(0);
        if (remaining > 0)
        {
            await dataBuffer.WriteAsync(internalBuffer.AsMemory(offset, remaining), cancellationToken);
        }

        return messages;
    }

    /// <summary>
    /// Continually polls the network stream for incoming data and raises events when full messages are received.
    /// 
    /// Incomplete messages are buffered until complete data becomes available.
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel polling.</param>
    /// <returns>A task that represents the asynchronous polling operation.</returns>
    private async Task PollForDataAsync(CancellationToken cancellationToken = default)
    {
        Memory<byte> buffer = new byte[this.configuration.NetworkBufferSize];
        using MemoryStream dataBuffer = new();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!this.stream.DataAvailable)
                {
                    await Task.Delay(this.configuration.PollDelayMs, cancellationToken);
                    continue;
                }

                int readBytesCount = await this.stream.ReadAsync(buffer, cancellationToken);
                if (readBytesCount == 0)
                {
                    // remote side has closed the connection
                    break;
                }

                await dataBuffer.WriteAsync(buffer[0..readBytesCount], cancellationToken);

                List<ReadOnlyMemory<byte>> messages = await this.ExtractAllMessagesAsync(dataBuffer, cancellationToken);

                foreach (var message in messages)
                {
                    if (!this.configuration.MessageProtocol.IsAliveMessage(message) || !this.configuration.FilterAliveMessages)
                    {
                        this.FireOnDataReceived(new NetworkStreamDataReceivedEventArgs(message));
                    }
                } 
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception exception)
        {
            this.Fail(exception);
        }
        finally
        {
            if (this.State != LifecycleState.Stopped)
            {
                this.Stop();
            }
        }
    }
}
