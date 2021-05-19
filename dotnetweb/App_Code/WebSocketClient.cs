using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Example
{

    public class WsClient : IDisposable
    {

        public void Dispose() => DisconnectAsync().Wait();

        private ClientWebSocket clientWs;
        private CancellationTokenSource cts;
        private Action<object> _onData;
        private Action<object> _onError;
        private Action _onDone;
        private bool _cancelOnError;
        public int ReceiveBufferSize { get; set; } = 8192;

        public async Task<WsClient> ConnectAsync(string url)
        {
            if (clientWs != null)
            {
                if (clientWs.State == WebSocketState.Open) return this;
                else clientWs.Dispose();
            }
            clientWs = new ClientWebSocket();
            if (cts != null) cts.Dispose();
            cts = new CancellationTokenSource();
            await clientWs.ConnectAsync(new Uri(url), cts.Token);
            return this;
        }

        public async Task<WsClient> ListenAsync(Action<object> onData, Action<object> onError = null, Action onDone = null, bool cancelOnError = true)
        {
            _onData = onData;
            _onDone = onDone;
            _onError = onError;
            _cancelOnError = cancelOnError;
            await Task.Factory.StartNew(ReceiveLoop, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            return this;
        }
        public async Task DisconnectAsync()
        {
            if (clientWs is null) return;
            // TODO: requests cleanup code, sub-protocol dependent.
            if (clientWs.State == WebSocketState.Open)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                await clientWs.CloseOutputAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
                await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            clientWs.Dispose();
            clientWs = null;
            cts.Dispose();
            cts = null;
        }

        private async Task ReceiveLoop()
        {
            var loopToken = cts.Token;
            WebSocketReceiveResult receiveResult = null;
            var buffer = new ArraySegment<byte>(new byte[2048]);
            try
            {
                while (!loopToken.IsCancellationRequested)
                {
                    using (var ms = new MemoryStream(ReceiveBufferSize))
                    {
                        do
                        {
                            receiveResult = await clientWs.ReceiveAsync(buffer, cts.Token);
                            if (receiveResult.MessageType != WebSocketMessageType.Close)
                            {
                                ms.Write(buffer.Array, buffer.Offset, receiveResult.Count);
                            }
                            else break;
                        }
                        while (!receiveResult.EndOfMessage);
                        if (_onData != null)
                        {
                            try
                            {
                                ms.Seek(0, SeekOrigin.Begin);
                                using (var reader = new StreamReader(ms, Encoding.UTF8))
                                {
                                    _onData(await reader.ReadToEndAsync());
                                }
                            }
                            catch { }
                        }
                        if (receiveResult.MessageType == WebSocketMessageType.Close) break;
                    }
                }
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is Exception)  
            { 
                if (_onError != null)
                {
                    try
                    {
                        _onError(ex);
                    }
                    catch
                    {

                    }
                }
            }
            finally
            {
                if (_onDone != null)
                {
                    try
                    {
                        _onDone();
                    }
                    catch
                    {

                    }
                }
            }
        }

        private async Task SendMessageAsync(string message)
        {
            ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
            await clientWs.SendAsync(bytesToSend, WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }

}