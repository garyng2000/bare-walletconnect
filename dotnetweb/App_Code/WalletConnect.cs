using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace GN.WalletConnect
{
    public static class Extension
    {
        public static string ToHexString(this byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", "").ToLower();
        }
        public static byte[] ToBytes(this string hexString)
        {

            return Enumerable.Range(0, hexString.Length / 2)
                                 .Select(x => Convert.ToByte(hexString.Substring(x * 2, 2), 16))
                                 .ToArray();
        }
        public static long ToUnixTime(this DateTime time)
        {
            var utc0 = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            return (long)DateTime.SpecifyKind(time, DateTimeKind.Utc).Subtract(utc0).TotalSeconds;
        }

        public static DateTime FromUnixTime(this long SecSince1970)
        {
            var utc = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(SecSince1970);
            return utc;
        }
        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, int timeoutSec)
        {
            if (timeoutSec <= 0) return await task;

            using (var timeoutCancellationTokenSource = new CancellationTokenSource())
            {
                var timerTask = Task.Delay(TimeSpan.FromSeconds(timeoutSec), timeoutCancellationTokenSource.Token);
                var completedTask = await Task.WhenAny(task, timerTask);
                if (completedTask == task)
                {
                    timeoutCancellationTokenSource.Cancel();
                    return await task;  // Very important in order to propagate exceptions
                }
                else
                {
                    throw new TimeoutException("The operation has timed out.");
                }
            }
        }
        public static object JContainerToSystemObject(this Newtonsoft.Json.Linq.JContainer c, bool useBigInteger = false)
        {
            Func<Newtonsoft.Json.Linq.JObject, object> jObjToSys = null;
            Func<Newtonsoft.Json.Linq.JArray, object> jArrayToSys = null;
            Func<Newtonsoft.Json.Linq.JToken, object> jtToSys = (jt) =>
            {
                var t = jt.Type.ToString();
                if (t == "String") return jt.ToObject<String>();
                else if (t == "Integer") return useBigInteger ? (object)jt.ToObject<System.Numerics.BigInteger>() : (object)jt.ToObject<int>();
                else if (t == "Boolean") return jt.ToObject<bool>();
                else if (t == "Float") return jt.ToObject<double>();
                else if (jt is Newtonsoft.Json.Linq.JArray) return jArrayToSys(jt as Newtonsoft.Json.Linq.JArray);
                else if (jt is Newtonsoft.Json.Linq.JObject) return jObjToSys(jt as Newtonsoft.Json.Linq.JObject);
                else return jt.ToObject<string>();
            };

            jArrayToSys = (a =>
            {
                return a.Select(jtToSys).ToList();
            });

            jObjToSys = (o =>
            {
                Dictionary<string, object> d = new System.Collections.Generic.Dictionary<string, object>();
                foreach (var zzx in o as Newtonsoft.Json.Linq.JObject)
                {
                    d.Add(zzx.Key, jtToSys(zzx.Value));
                };
                return d;
            });

            if (c is Newtonsoft.Json.Linq.JArray)
            {
                return jArrayToSys(c as Newtonsoft.Json.Linq.JArray);
            }
            else if (c is Newtonsoft.Json.Linq.JObject)
            {
                return jObjToSys(c as Newtonsoft.Json.Linq.JObject);
            }
            else
            {
                throw new Exception(string.Format("{0} is not Array or Object byt {1}, not valid json source", c, c.GetType()));
            }
        }
        public static Tout Maybe<Tin, Tout>(this Tin instance, Func<Tin, Tout> Output)
        {
            if (instance == null)
                return default(Tout);
            else
                return Output(instance);
        }
        public static TValue GetOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue = default(TValue))
        {
            if (dictionary != null && dictionary.ContainsKey(key))
            {
                return dictionary[key];
            }
            return defaultValue;
        }
    }
    public class WsClient : IDisposable
    {

 
        private ClientWebSocket clientWs;
        private CancellationTokenSource cts;
        private Action<object> _onData;
        private Action<Exception> _onError;
        private Action _onDone;
        private bool disposedValue;
        private Task listener;
        public int ReceiveBufferSize { get; set; } = 8192;

        public async Task ConnectAsync(string url)
        {
            if (clientWs != null)
            {
                if (clientWs.State == WebSocketState.Open) return;
                else clientWs.Dispose();
            }
            clientWs = new ClientWebSocket();
            if (cts != null) cts.Dispose();
            cts = new CancellationTokenSource();
            await clientWs.ConnectAsync(new Uri(url), cts.Token);
        }

        public void StartListener(Action<object> onData, Action<Exception> onError = null, Action onDone = null)
        {
            _onData = onData;
            _onDone = onDone;
            _onError = onError;
            if (listener == null)
            {
                listener = Task.Factory.StartNew(ReceiveLoop, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                //listener = Task.Run(ReceiveLoop, cts.Token);
            }
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
            _onData = null;
            _onError = null;
            _onDone = null;
            listener = null;
        }

        private async Task ReceiveLoop()
        {
            var loopToken = cts.Token;
            WebSocketReceiveResult receiveResult = null;
            var buffer = new ArraySegment<byte>(new byte[ReceiveBufferSize]);
            try
            {
                while (!loopToken.IsCancellationRequested)
                {
                    using (var ms = new MemoryStream())
                    {
                        do
                        {
                            receiveResult = await clientWs.ReceiveAsync(buffer, cts.Token);
                            if (receiveResult.MessageType != WebSocketMessageType.Close)
                            {
                                ms.Write(buffer.Array, 0, receiveResult.Count);
                            }
                            else break;
                        }
                        while (!receiveResult.EndOfMessage);
                        if (_onData != null)
                        {
                            try
                            {
                                //ms.Seek(0, SeekOrigin.Begin);
                                _onData(System.Text.UTF8Encoding.UTF8.GetString(ms.ToArray()));
                                //using (var reader = new StreamReader(ms, Encoding.UTF8))
                                //{
                                //    _onData(await reader.ReadToEndAsync());
                                //}
                            }
                            catch { }
                        }
                        if (receiveResult.MessageType == WebSocketMessageType.Close) break;
                    }
                }
            }
            catch (Exception ex) when (ex is TaskCanceledException || ex is Exception)
            {
                if (_onError != null && !(ex is TaskCanceledException))
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

        public Task SendMessageAsync(string message)
        {
            ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(message));
            return clientWs.SendAsync(bytesToSend, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    DisconnectAsync().Wait();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~WsClient()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
    public class WCUri
    {
        public string topic;
        public int version;
        public string bridgeUrl;
        public string keyHex;

        public override string ToString()
        {
            var encodedBridgeUrl = HttpUtility.UrlEncode(bridgeUrl);
            return string.Format("wc:{0}@{1}?bridge={2}&key={3}"
                , topic, version, encodedBridgeUrl, keyHex);
        }
        public string UniversalLink(string appLink)
        {
            return appLink +
                (appLink.EndsWith("/") ? "" : "/") +
                "wc?uri=" +
                HttpUtility.UrlEncode(string.Format("{0}", this));
        }
        public string DeepLink(string appLink)
        {
            return appLink +
                (appLink.EndsWith(":") ? "//" : (appLink.EndsWith("/") ? "" : "/")) +
                "wc?uri=" +
                HttpUtility.UrlEncode(string.Format("{0}", this));
        }
        public static WCUri FromString(string wcUrl)
        {
            var rx = new Regex(@"wc:([^@]+)@(\d+)\?bridge=([^&]+)&key=([0-9a-fA-F]+)");
            var match = rx.Match((wcUrl ?? "").Trim());
            if (match != null)
            {
                return new WCUri()
                {
                    topic = match.Groups[1].Value,
                    version = int.Parse(match.Groups[2].Value),
                    bridgeUrl = HttpUtility.UrlDecode(match.Groups[3].Value),
                    keyHex = match.Groups[4].Value
                };

            }
            else
            {
                throw new Exception("malformed walletconnect uri");
            }
        }
    }
    public class JsonRpc
    {
        public long? id;
        public string jsonrpc = "2.0";
        public string method;
        [JsonProperty("params")]
        public List<dynamic> parameters;
        public dynamic result;
        public dynamic error;

        public override string ToString()
        {
            return ToJson();
        }
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }
        public static JsonRpc FromString(string jsonString)
        {
            //var converter = new ExpandoObjectConverter();
            var jsonrpc  = JsonConvert.DeserializeObject<JsonRpc>(jsonString);
            jsonrpc.result = (jsonrpc.result as JContainer).Maybe(o => o.JContainerToSystemObject());
            jsonrpc.error = (jsonrpc.error as JContainer).Maybe(o => o.JContainerToSystemObject());
            jsonrpc.parameters = jsonrpc.parameters.Maybe(l => l.Select(v => ((JContainer)v).JContainerToSystemObject()).ToList());
            return jsonrpc;
        }
    }
    public class WCPubSub
    {
        public string topic;
        public string type;
        public string payload;
        public bool silent;

        public override string ToString()
        {
            return ToJson();
        }
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings
            {
 //               NullValueHandling = NullValueHandling.Ignore
            });
        }
        public static WCPubSub FromString(string jsonString)
        {
            return JsonConvert.DeserializeObject<WCPubSub>(jsonString);
        }
    }

    public class WCPayload
    {
        public string data;
        public string hmac;
        public string iv;
        public override string ToString()
        {
            return ToJson();
        }
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
        }
        public static WCPayload FromString(string jsonString)
        {
            return JsonConvert.DeserializeObject<WCPayload>(jsonString);
        }
    }

    public class WCConnectionRequest
    {
        public string wcUri;
        public WCSession wcSession;
        public KeyValuePair<long, Task<Object>> wcSessionRequest;
        public Task<Object> requestResponse;
    }

    public class WCSession: IDisposable
    {
        public string sessionTopic;
        public WsClient wsClient;
        public string keyHex;
        public string ourPeerId;
        public Dictionary<string, List<Func<WCSession, JsonRpc, JsonRpc>>> eventHandler;
        public int? chainId;
        public string bridgeUrl;
        public bool isConnected = false;
        public bool isActive = false;
        Dictionary<long, KeyValuePair<JsonRpc, TaskCompletionSource<dynamic>>> outstandingRpc = new Dictionary<long, KeyValuePair<JsonRpc, TaskCompletionSource<dynamic>>>();

        public int? theirChainId;
        public List<string> theirAccounts;
        public string theirRpcUrl;
        public string theirPeerId;
        public dynamic theirMeta;
        private bool disposedValue;

        #region Helper

        #endregion
        #region Crypto
        public static byte[] RandomBytes(int count)
        {
            byte[] buffer = new byte[count];
            RNGCryptoServiceProvider rand = new RNGCryptoServiceProvider();
            rand.GetBytes(buffer);
            return buffer;
        }
        public static byte[] AesEncrypt(string data, byte[] key, byte[] iv)
        {
            using (AesManaged aes = new AesManaged())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;

                ICryptoTransform enc = aes.CreateEncryptor(aes.Key, aes.IV);

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(data);
                        cs.Write(bytes, 0, bytes.Length);
                        cs.FlushFinalBlock();
                    }
                    return ms.ToArray();
                }
            }
        }
        public static byte[] AesDecrypt(byte[] data, byte[] key, byte[] iv)
        {
            using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;


                ICryptoTransform dec = aes.CreateDecryptor(aes.Key, aes.IV);

                using (MemoryStream ms = new MemoryStream(data))
                {
                    using (MemoryStream sink = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, dec, CryptoStreamMode.Read))
                        {
                            int read = 0;
                            byte[] buffer = new byte[1024];
                            do
                            {
                                read = cs.Read(buffer, 0, buffer.Length);

                                if (read > 0)
                                    sink.Write(buffer, 0, read);
                            } while (read > 0);

                            cs.Flush();
                            sink.Flush();
                            return sink.ToArray();
                        }
                    }
                }
            }
        }
        public static string GetWCHMAC(byte[] data, byte[] key, byte[] iv)
        {
            // HMAC-SHA256
            using (var hmac = new HMACSHA256(key)) 
            {
                hmac.Initialize();
                byte[] toSign = new byte[iv.Length + data.Length];

                //copy our 2 array into one
                Buffer.BlockCopy(data, 0, toSign, 0, data.Length);
                Buffer.BlockCopy(iv, 0, toSign, data.Length, iv.Length);

                var sig = hmac.ComputeHash(toSign);
                return sig.ToHexString();
            }
        }

        public static WCPayload WCEncrypt(string payload, string keyHex, string ivHex)
        {
            var iv = ivHex.ToBytes();
            var key = keyHex.ToBytes();
            var encrypted = AesEncrypt(payload, key, iv);
            return new WCPayload() { data = encrypted.ToHexString(), hmac = GetWCHMAC(encrypted, key, iv), iv = ivHex };
        }

        public static string WCDecrypt(string dataHex, string keyHex, string ivHex, string dataSig) {
            var iv = ivHex.ToBytes();
            var key = keyHex.ToBytes(); 
            var data = dataHex.ToBytes();
            var hmac = GetWCHMAC(data, key, iv);
            if (dataSig != hmac)
            {
                throw new Exception("hmac not match");
            }
            var decrypted = AesDecrypt(data, key, iv);
            return System.Text.UTF8Encoding.UTF8.GetString(decrypted);
        }
        #endregion
        #region WC PubSub
        public static JsonRpc DecodePubSubMessage(string message, string keyHex)
        {
            var pubsub = WCPubSub.FromString(message);
            var payload = WCPayload.FromString(pubsub.payload);
            var wcRequest = WCDecrypt(payload.data, keyHex, payload.iv, payload.hmac);
            var jsonRpc = JsonRpc.FromString(wcRequest);
            return jsonRpc;
        }

        public async Task<KeyValuePair<long, Task<object>>> SendRequestAsync(string method, List<object> parameters, string peerId = null, int timeoutSec = 0)
        {
            if (!isConnected && method != "wc_sessionRequest")
            {
                await Connect();
            }
            long id = DateTime.UtcNow.ToUnixTime() * 1000; // in ms
            var jsonRpc = new JsonRpc() { id = id, method = method, parameters = parameters };
            string ivHex = RandomBytes(16).ToHexString();
            var wcRequest = WCEncrypt(jsonRpc.ToJson(), keyHex, ivHex);
            WCDecrypt(wcRequest.data, keyHex, wcRequest.iv, wcRequest.hmac);
            var wcRequestPub = new WCPubSub() { topic = peerId ?? theirPeerId, payload = wcRequest.ToJson(), silent = true, type = "pub" };
            var completer = new TaskCompletionSource<dynamic>();
            if (method != "wc_sessionUpdate")
            {
                outstandingRpc[id] = new KeyValuePair<JsonRpc, TaskCompletionSource<dynamic>>(jsonRpc, completer);
            }
            await wsClient.SendMessageAsync(wcRequestPub.ToJson());
            var requestResult = completer.Task.TimeoutAfter(timeoutSec);
            return new KeyValuePair<long, Task<object>>(id, requestResult);
        }

        public async Task<KeyValuePair<long, Task<object>>> SendResponseAsync(long id, string method, dynamic result = null, dynamic error = null)
        {
            if (!isConnected && method != "wc_sessionRequest")
            {
                await Connect();
            }
            var jsonRpc = new JsonRpc() { id = id, result = result, error = error };
            string ivHex = RandomBytes(16).ToHexString();
            var wcResponse = WCEncrypt(jsonRpc.ToJson(), keyHex, ivHex);
            var wcResponsePub = new WCPubSub() { topic = theirPeerId, payload = wcResponse.ToJson(), silent = true, type = "pub" };
            await wsClient.SendMessageAsync(wcResponsePub.ToJson());
            return new KeyValuePair<long, Task<object>>(id, Task.FromResult((object) true));
        }

        public async Task<KeyValuePair<long, Task<object>>> SendSessionRequestAsync(dynamic myMeta, int timeoutSec = 0)
        {
            var wcSessionRequestParams = new List<dynamic>()
            {
                new { peerId = ourPeerId, peerMeta = myMeta, chainId = chainId }
            };
            var request = await SendRequestAsync("wc_sessionRequest", wcSessionRequestParams, sessionTopic, timeoutSec);
            return request;
        }
        
        public async Task<KeyValuePair<long, Task<object>>> SendSessionRequestResponse(JsonRpc sessionRequest, dynamic myMeta
            , List<string> accounts = null, bool approved = true
            , string rpcUrl = null, int? chainId = null, bool ssl = true)
        {
            var wcSessionRequestResult = new {
                                          approved =  approved,
                                          accounts = accounts ?? new List<string>(),
                                          rpcUrl = rpcUrl,
                                          ssl =  ssl,
                                          networkId = chainId,
                                          peerId = ourPeerId,
                                          peerMeta = myMeta,
                                          chainId =chainId
                                        };
            var response = SendResponseAsync(sessionRequest.id.Value, sessionRequest.method, wcSessionRequestResult);
            if (approved)
            {
                isConnected = true;
                isActive = true;
                var wcSub = new WCPubSub() { topic = ourPeerId, payload ="{}", type = "sub", silent = true };
                // listen on our channel for message
                await wsClient.SendMessageAsync(wcSub.ToJson());
            }
            return await response;
        }
        #endregion

        public void SetEventHandler(string method, Func<WCSession, JsonRpc, JsonRpc> handler, bool remove = false)
        {
            List<Func<WCSession,JsonRpc,JsonRpc>> handlers = null;
            if (eventHandler.ContainsKey(method))
            {
                handlers = eventHandler[method];
            }
            else
            {
                handlers = new List<Func<WCSession, JsonRpc, JsonRpc>>();
            }
            if (!remove)
            {
                handlers.Add(handler);
            }
            else if (handlers.Contains(handler)) {
                handlers.Remove(handler);
            }
        }
        public override string ToString()
        {
            var obj = new {
                  ourPeerId= ourPeerId,
                  theirPeerId= theirPeerId,
                  theirMeta= theirMeta,
                  isConnected= isConnected,
                  isActive= isActive,
                  theirChainId= theirChainId,
                  theirRpcUrl= theirRpcUrl,
                  accounts= theirAccounts,
                  bridgeUrl= bridgeUrl
                };
            return JsonConvert.SerializeObject(obj);
        }

        private void StartListener(Action<WCSession, JsonRpc> sessionRequestHandler)
        {
            wsClient.StartListener(
                (message) => {
                    try
                    {
                        var jsonRpc = DecodePubSubMessage(message as string, keyHex);
                        var method = jsonRpc.method;

                        if (method == "wc_sessionRequest")
                        {
                            var parameters = jsonRpc.parameters;
                            if (parameters != null && parameters.Count > 0)
                            {
                                Dictionary<string, object> request = parameters[0];
                                theirMeta = request.GetOrDefault("peerMeta");
                                theirPeerId = request.GetOrDefault("peerId") as string;
                                theirChainId = request.GetOrDefault("chainId") as int?;
                                theirRpcUrl = request.GetOrDefault("rpcUrl") as string;
                                theirAccounts = request.GetOrDefault("accounts") as List<string>;
                            }
                            if (sessionRequestHandler != null)
                            {
                                sessionRequestHandler(this, jsonRpc);
                            }
                        }
                        else if (method == "wc_sessionUpdate")
                        {
                            var parameters = jsonRpc.parameters;
                            if (parameters != null && parameters.Count > 0)
                            {
                                Dictionary<string, object> request = parameters[0];
                                isConnected = (bool) request.GetOrDefault("approved", false);
                                isActive = isActive && isConnected;
                                theirChainId = request.GetOrDefault("chainId") as int?;
                                theirRpcUrl = request.GetOrDefault("rpcUrl") as string;
                                theirAccounts = request.GetOrDefault("accounts") as List<string>;
                            }
                        }

                        if (method != "wc_sessionRequest" ||
                            (eventHandler != null &&
                            eventHandler.ContainsKey("wc_sessionRequest") &&
                            eventHandler["wc_sessionRequest"].Count > 0))
                        {
                            ProcessMessage(jsonRpc);
                        }

                    }
                    catch (Exception ex)
                    {
                        var x = ex.Message;
                    }
                },
                (error) => {
                },
                () => {
                    isConnected = false;
                    isActive = false;
                });

        }
        public async Task<WCSession> Connect(string topic = null, Action<WCSession, JsonRpc> sessionRequestHandler = null)
        {
            try
            {
                var wsUrl = new Regex(@"^http").Replace(bridgeUrl, "ws");
                var subTopic = topic ?? ourPeerId;
                var wcSessionSub = new WCPubSub() { topic = subTopic, payload = "{}", type = "sub", silent = true };
                wsClient = new WsClient();
                await wsClient.ConnectAsync(wsUrl);
                StartListener(sessionRequestHandler);
                // listen on our channel for message
                await wsClient.SendMessageAsync(wcSessionSub.ToJson());

                return this;
            }
            catch
            {
                throw;
            }
        }

        public async static Task<WCConnectionRequest> CreateSession(string bridgeUrl, dynamic myMeta
                                , Dictionary<string, List<Func<WCSession, JsonRpc, JsonRpc>>> requestHandler = null
                                , int chainId = 1, int timeoutSec = 0)
        {
            var sessionTopic = Guid.NewGuid().ToString();
            var myPeerId = Guid.NewGuid().ToString();
            var keyHex = RandomBytes(32).ToHexString();
            var wcVersion = 1;
            var wcUri = new WCUri() { bridgeUrl = bridgeUrl, keyHex = keyHex, topic = sessionTopic, version = wcVersion };
            var wcSession = new WCSession() { bridgeUrl = bridgeUrl, sessionTopic = sessionTopic, keyHex = keyHex, ourPeerId = myPeerId, eventHandler = requestHandler };
            await wcSession.Connect();
            var sessionRequest = await wcSession.SendSessionRequestAsync(myMeta, timeoutSec);
            return new WCConnectionRequest() { wcUri = wcUri.ToString(), wcSession = wcSession, wcSessionRequest = sessionRequest };
        }
        public async static Task<KeyValuePair<WCSession, JsonRpc>> ConnectSession(string wcUrl, dynamic myMeta
                                , Dictionary<string, List<Func<WCSession, JsonRpc, JsonRpc>>> requestHandler = null
                                , int chainId = 1, int timeoutSec = 0)
        {
            TaskCompletionSource<KeyValuePair<WCSession, JsonRpc>> completer = new TaskCompletionSource<KeyValuePair<WCSession, JsonRpc>>();
            Action<WCSession, JsonRpc> requestRetrieved = (session, jsonrpc) =>
            {
                completer.SetResult(new KeyValuePair<WCSession, JsonRpc>(session, jsonrpc));
            };
            var wcUri = WCUri.FromString(wcUrl);
            var myPeerId = Guid.NewGuid().ToString();
            var keyHex = wcUri.keyHex;
            var wcVersion = wcUri.version;
            var bridgeUrl = wcUri.bridgeUrl;
            var sessionTopic = wcUri.topic;
            var wcSession = new WCSession() { bridgeUrl = bridgeUrl, sessionTopic = sessionTopic, keyHex = keyHex, ourPeerId = myPeerId, eventHandler = requestHandler };
            await wcSession.Connect(sessionTopic, requestRetrieved);
            return await completer.Task.TimeoutAfter(timeoutSec);
        }

        #region GC related
        public async Task Close()
        {
            await wsClient.DisconnectAsync();
            return;
        }

        public async Task Destroy()
        {
            var param = new List<dynamic>() {
                new { approved = false, chainId = chainId, accounts = new List<string>(), rpcUrl = (string) null }
            };
            await SendRequestAsync("wc_sessionUpdate", param);
            await Close();
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    wsClient.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~WCSession()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion

        private void ProcessMessage(JsonRpc jsonRpc)
        {
            var id = jsonRpc.id;
            var method = jsonRpc.method;
            var parameters = jsonRpc.parameters;
            var result = jsonRpc.result;
            var error = jsonRpc.error;
            bool handled = false;
            Exception internalError = null;
            bool hasHandler = false;
            if (method != null)
            {
                if (eventHandler != null && eventHandler.ContainsKey(method))
                {
                    var handlers = eventHandler[method] ?? new List<Func<WCSession, JsonRpc, JsonRpc>>();
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            hasHandler = true;
                            handler(this, jsonRpc);
                            handled = true;
                        }
                        catch (Exception ex)
                        {
                            internalError = ex;
                        }
                    }
                }
                if (!hasHandler && internalError != null && eventHandler.ContainsKey("_"))
                {
                    var handlers = eventHandler["_"] ?? new List<Func<WCSession, JsonRpc, JsonRpc>>();
                    foreach (var handler in handlers)
                    {
                        try
                        {
                            handler(this, jsonRpc);
                            handled = true;
                        }
                        catch (Exception ex)
                        {
                            internalError = ex;
                        }
                    }
                }
                if (!handled || internalError != null)
                {
                    dynamic errorresponse = new
                    {
                        id = jsonRpc.id,
                        error = new
                        {
                            code = internalError != null ? -32063 : -32601,
                            message = internalError != null ? internalError.Message : "method not found"
                        }
                    };
                    try
                    {
                        // intended run and not wait
                        if (id != null) _ = SendResponseAsync(id.Value, method, error = errorresponse);
                    }
                    catch
                    {

                    }
                }
            }
            else if (result != null || error != null)
            {
                if (id != null && outstandingRpc.ContainsKey(id.Value))
                {
                    var request = outstandingRpc[id.Value];
                    var requestedMethod = request.Key.method;
                    var completer = request.Value;
                    outstandingRpc.Remove(id.Value);
                    if (result != null)
                    {
                        if (requestedMethod == "wc_sessionRequest")
                        {
                            theirMeta = result["peerMeta"];
                            theirPeerId = result["peerId"] as string;
                            theirChainId = result["chainId"] as int?;
                            theirRpcUrl = result["rpcUrl"] as string;
                            theirAccounts = result["accounts"] as List<string> ?? new List<string>();
                            isActive = (bool)result["approved"];
                            isConnected = isActive;

                        }
                        completer.SetResult(result);
                    }
                    else
                    {
                        completer.SetException(new Exception(JsonConvert.SerializeObject(error)));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Summary description for WalletConnect
    /// </summary>
    public class WalletConnect
    {
        public WalletConnect()
        {
            //
            // TODO: Add constructor logic here
            //
        }
    }
}
