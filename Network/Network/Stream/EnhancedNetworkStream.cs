﻿using Network.Architecture;
using Network.Architecture.Interfaces;
using Network.Architecture.Interfaces.Protocol;
using Network.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Network.Stream;

public class EnhancedNetworkStream<TMessage> : LifecycleComponent, IMessageSender<TMessage>
{
    private NetworkStream stream;
    private EnhancedNetworkStreamConfiguration<TMessage> configuration;

    private CancellationTokenSource? cancellationTokenSource;

    public EnhancedNetworkStream(NetworkStream networkStream, EnhancedNetworkStreamConfiguration<TMessage> configuration)
    {
        this.stream = networkStream;
        this.configuration = configuration;
        this.state = LifecycleState.Initialized;
    }

    public event EventHandler<EnhancedNetworkStreamDataReceivedEventArgs>? DataReceived;

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

    public override void Stop()
    {
        if (this.state != LifecycleState.Started)
        {
            throw new InvalidOperationException("Network stream is to running.");
        }

        this.cancellationTokenSource?.Cancel();
        this.cancellationTokenSource = null;
    }

    public void Send(TMessage message)
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

    public async Task SendAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            ReadOnlyMemory<byte> encodedMessage = this.configuration.MessageProtocol.Encode(message);
            await this.stream.WriteAsync(encodedMessage, cancellationToken);
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

    protected virtual void FireOnDataReceived(EnhancedNetworkStreamDataReceivedEventArgs e)
    {
        this.DataReceived?.Invoke(this, e);
    }

    private async Task PollUntilFullMessage(int messageSize, MemoryStream dataBuffer, Memory<byte> networkBuffer, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!this.stream.DataAvailable)
            {
                await Task.Delay(this.configuration.PollDelayMs, cancellationToken);
                continue;
            }

            int readBytesCount = await this.stream.ReadAsync(networkBuffer, cancellationToken);
            if (readBytesCount == 0)
            {
                throw new InvalidOperationException("Socket connection probably closed.");
            }

            await dataBuffer.WriteAsync(networkBuffer[0..readBytesCount], cancellationToken);
            if (dataBuffer.Length >= messageSize)
            {
                return;
            }
        }
    }

    private async Task PollForDataAsync(CancellationToken cancellationToken = default)
    {
        Memory<byte> buffer = new byte[this.configuration.NetworkBufferSize];
        MemoryStream dataBuffer = new MemoryStream();

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
                    break;
                }

                int messageSize = this.configuration.MessageProtocol.GetMessageSize(buffer[0..readBytesCount]);
                await dataBuffer.WriteAsync(buffer[0..readBytesCount], cancellationToken);
                if (messageSize != readBytesCount)
                {
                    await this.PollUntilFullMessage(messageSize, dataBuffer, buffer, cancellationToken);
                }

                byte[] receivedData = dataBuffer.GetBuffer();
                ReadOnlyMemory<byte> data = receivedData.AsMemory(0, messageSize);
                if (!this.configuration.MessageProtocol.IsAliveMessage(data))
                {
                    this.FireOnDataReceived(new EnhancedNetworkStreamDataReceivedEventArgs(data));
                }

                byte[] unusedData = receivedData[messageSize..];

                // get unused bytes
                dataBuffer = new MemoryStream(unusedData);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch
        {
            // Exception Handling
        }
        finally
        {
            this.State = LifecycleState.Stopped;
        }
    }
}
