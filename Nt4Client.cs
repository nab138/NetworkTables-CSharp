using System;
using System.Collections.Generic;
using MessagePack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp;
using Random = System.Random;

namespace NetworkTablesSharp
{
    /// <summary>
    /// Create a new NT4Client.
    /// </summary>
    /// <param name="appName">The name of the application to connect as (default "Nt4Unity")</param>
    /// <param name="serverBaseAddress">The IP address of the server to connect to (default 127.0.0.1)</param>
    /// <param name="serverPort">The port to connect to the server on (default 5810)</param>
    /// <param name="onOpen">On Open event handler</param>
    /// <param name="onNewTopicData">On New Topic Data event handler</param>
    public class Nt4Client
    {
        /// <summary>
        /// Convert a type string to its corresponding integer.
        /// </summary>
        public static readonly Dictionary<string, int> TypeStrIdxLookup = new Dictionary<string, int>()
        {
            { "boolean", 0 },
            { "double", 1 },
            { "int", 2 },
            { "string", 3 },
            { "json", 4 },
            { "raw", 5 },
            { "rpc", 5 },
            {"msgpack", 5},
            {"protobuf", 5},
            {"boolean[]", 16},
            {"double[]", 17},
            {"int[]", 18},
            {"float[]", 19},
            {"string[]", 20},
        };

        private readonly string _serverAddress;
        private readonly EventHandler? _onOpen;
        private readonly Action<Nt4Topic, long, object>? _onNewTopicData;
        private readonly string _appName;

        public Nt4Client(string appName, string serverBaseAddress, int serverPort = 5810, EventHandler? onOpen = null, Action<Nt4Topic, long, object>? onNewTopicData = null)
        {
            _serverAddress = "ws://" + serverBaseAddress + ":" + serverPort + "/nt/" + appName;
            _onOpen = onOpen;
            _onNewTopicData = onNewTopicData;
            _appName = appName;
        }
        
        
        private WebSocket? _ws;
        private long? _serverTimeOffsetUs;
        private long _networkLatencyUs;

        private readonly Dictionary<int, Nt4Subscription> _subscriptions = new Dictionary<int, Nt4Subscription>();
        private readonly Dictionary<string, Nt4Topic> _publishedTopics = new Dictionary<string, Nt4Topic>();
        private readonly Dictionary<string, Nt4Topic> _serverTopics = new Dictionary<string, Nt4Topic>();

        /// <summary>
        /// Connect to the NetworkTables server.
        /// </summary>
        /// <returns>Whether or not the connection was successful, and an error message if it was not</returns>
        public (bool success, string? errorMessage) Connect()
        {
            try
            {
                if (_ws != null && (_ws.ReadyState == WebSocketState.Open || _ws.ReadyState == WebSocketState.Connecting))
                {
                    return (true, null);
                }
                _ws = new WebSocket(_serverAddress);
                _ws.OnOpen += OnOpen;
                _ws.OnMessage += OnMessage;
                _ws.OnError += OnError;
                _ws.OnClose += OnClose;
                _ws.Connect();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
        
        /// <summary>
        /// Disconnect from the NetworkTables server.
        /// </summary>
        public void Disconnect()
        {
            if (Connected())
            {
                _ws!.Close();
            }
        }
        
        private void OnOpen(object? sender, EventArgs e)
        {
            Console.WriteLine("[NT4] Connected with identity " + _appName);
            _onOpen?.Invoke(sender, e);
            WsSendTimestamp();
        }

        private void OnMessage(object? message, MessageEventArgs args)
        {
            if (args.Data != null)
            {
                // Attempt to decode the message as JSON array
                object[]? msg = JsonConvert.DeserializeObject<object[]>(args.Data);
                if (msg == null)
                {
                    Console.WriteLine("[NT4] Failed to decode JSON message: " + message);
                    return;
                }
                // Iterate through the messages
                foreach (object obj in msg)
                {
                    string? objStr = obj.ToString();
                    if(objStr == null) continue;
                    // Attempt to decode the message as a JSON object
                    Dictionary<string, object>? msgObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(objStr);
                    if (msgObj == null)
                    {
                        Console.WriteLine("[NT4] Failed to decode JSON message: " + obj);
                        continue;
                    }
                    // Handle the message
                    HandleJsonMessage(msgObj);
                }
            }
            else
            {
                object[]? msg = MessagePackSerializer.Deserialize<object[]>(args.RawData);
                if (msg == null)
                {
                    Console.WriteLine("[NT4] Failed to decode MSGPack message: " + message);
                    return;
                }
                HandleMsgPackMessage(msg);
            }
        }
        
        private void OnError(object? sender, WebSocketSharp.ErrorEventArgs e)
        {
            Console.WriteLine("[NT4] Error: " + e.Message + " " + e.Exception);
        }
        
        private void OnClose(object? sender, CloseEventArgs e)
        {
            Console.WriteLine("[NT4] Disconnected: " + e.Reason);
            _serverTopics.Clear();
            _publishedTopics.Clear();
            _subscriptions.Clear();
        }
        
        /// <summary>
        /// Publish a topic to the server.
        /// </summary>
        /// <param name="key">The topic to publish</param>
        /// <param name="type">The type of topic</param>
        public void PublishTopic(string key, string type)
        {
            PublishTopic(key, type, new Dictionary<string, object>());
        }
        
        /// <summary>
        /// Publish a topic to the server.
        /// </summary>
        /// <param name="key">The topic to publish</param>
        /// <param name="type">The type of topic</param>
        /// <param name="properties">Properties of the topic</param>
        public void PublishTopic(string key, string type, Dictionary<string, object> properties)
        {
            Nt4Topic topic = new Nt4Topic(GetNewUid(), key, type, properties);
            if (!Connected() || _publishedTopics.ContainsKey(key)) return;
            _publishedTopics.Add(key, topic);
            WsPublishTopic(topic);
        }

        /// <summary>
        /// Unpublish a topic from the server.
        /// </summary>
        /// <param name="key">The topic to unpublish</param>
        public void UnpublishTopic(string key)
        {
            if (!Connected()) return;
            if (!_publishedTopics.ContainsKey(key))
            {
                Console.WriteLine("[NT4] Attempted to unpublish topic that was not published: " + key);
                return;
            }
            Nt4Topic topic = _publishedTopics[key];
            _publishedTopics.Remove(key);
            WsUnpublishTopic(topic);
        }

        /// <summary>
        /// Publish a value to the server.
        /// </summary>
        /// <param name="key">The topic to publish the value under</param>
        /// <param name="value">The new value to publish</param>
        public void PublishValue(string key, object value)
        {
            long timestamp = GetServerTimeUs() ?? 0;
            if (!Connected()) return;
            if (!_publishedTopics.ContainsKey(key))
            {
                Console.WriteLine("[NT4] Attempted to publish value for topic that was not published: " + key);
                return;
            }
            Nt4Topic topic = _publishedTopics[key];
            WsSendBinary(MessagePackSerializer.Serialize(new[] {topic.Uid, timestamp, TypeStrIdxLookup[topic.Type], value}));
        }

        /// <summary>
        /// Subscribe to a topic from the server.
        /// </summary>
        /// <param name="key">The topic to subscribe to</param>
        /// <param name="periodic">Update rate in seconds</param>
        /// <param name="all">Whether or not to send all values or just the current ones</param>
        /// <param name="topicsOnly">Whether or not to read only topic announcements, not values.</param>
        /// <param name="prefix">Whether or not to include all sub-values</param>
        /// <returns>The ID of the subscription (for unsubscribing), -1 if not Connected()</returns>
        public int Subscribe(string key, double periodic = 0.1, bool all = false, bool topicsOnly = false, bool prefix = false)
        {
            if(!Connected()) return -1;
            Nt4SubscriptionOptions opts = new Nt4SubscriptionOptions(periodic, all, topicsOnly, prefix);
            Nt4Subscription sub = new Nt4Subscription(GetNewUid(), new string[] {key}, opts);
            WsSubscribe(sub);
            _subscriptions.Add(sub.Uid, sub);
            return sub.Uid;
        }
        
        /// <summary>
        /// Subscribe to a topic from the server.
        /// </summary>
        /// <param name="key">The topic to subscribe to</param>
        /// <param name="opts">Options for the subscription</param>
        /// <returns>The ID of the subscription (for unsubscribing), -1 if not Connected()</returns>
        public int Subscribe(string key, Nt4SubscriptionOptions opts)
        {
            if(!Connected()) return -1;
            Nt4Subscription sub = new Nt4Subscription(GetNewUid(), new string[] { key }, opts);
            WsSubscribe(sub);
            _subscriptions.Add(sub.Uid, sub);
            return sub.Uid;
        }
        
        /// <summary>
        /// Subscribe to a topic from the server.
        /// </summary>
        /// <param name="keys">A list of topics to subscribe to</param>
        /// <param name="periodic">Update rate in seconds</param>
        /// <param name="all">Whether or not to send all values or just the current ones</param>
        /// <param name="topicsOnly">Whether or not to read only topic announcements, not values.</param>
        /// <param name="prefix">Whether or not to include all sub-values</param>
        /// <returns>The ID of the subscription (for unsubscribing)</returns>
        public int Subscribe(string[] keys, double periodic = 0.1, bool all = false, bool topicsOnly = false, bool prefix = false)
        {
            if(!Connected()) return -1;
            Nt4SubscriptionOptions opts = new Nt4SubscriptionOptions(periodic, all, topicsOnly, prefix);
            Nt4Subscription sub = new Nt4Subscription(GetNewUid(), keys, opts);
            WsSubscribe(sub);
            _subscriptions.Add(sub.Uid, sub);
            return sub.Uid;
        }

        /// <summary>
        /// Unsubscribe from a subscription.
        /// </summary>
        /// <param name="uid">The ID of the subscription</param>
        public void Unsubscribe(int uid)
        {
            if(!Connected()) return;
            if (!_subscriptions.ContainsKey(uid))
            {
                Console.WriteLine("[NT4] Attempted to unsubscribe from a subscription that does not exist: " + uid);
                return;
            }
            Nt4Subscription sub = _subscriptions[uid];
            _subscriptions.Remove(uid);
            WsUnsubscribe(sub);
        }
        
        // ws utility functions
        private void WsSendJson(string method, Dictionary<string, object> paramsObj)
        {
            if (!Connected()) return;
            Dictionary<string, object> msg = new Dictionary<string, object>()
            {
                { "method", method },
                { "params", paramsObj }
            };
            // convert msg to a json array, not object, containing only msg
            _ws!.SendAsync(JsonConvert.SerializeObject(new object[] {msg}), null);
        }
        
        private void WsSendBinary(byte[] data)
        {
            if (!Connected())
            {
                Console.WriteLine("[NT4] Attempted to send data while the WebSocket was not open");
                return;
            }
            _ws!.SendAsync(data, null);
        }

        private void WsPublishTopic(Nt4Topic topic)
        {
            WsSendJson("publish", topic.ToPublishObj());
        }
        
        private void WsUnpublishTopic(Nt4Topic topic)
        {
            WsSendJson("unpublish", topic.ToUnpublishObj());
        }

        private void WsSubscribe(Nt4Subscription subscription)
        {
            WsSendJson("subscribe", subscription.ToSubscribeObj());
        }
        
        private void WsUnsubscribe(Nt4Subscription subscription)
        {
            WsSendJson("unsubscribe", subscription.ToUnsubscribeObj());
        }
        
        private void WsSendTimestamp()
        {
            long timestamp = GetClientTimeUs();
            // Send the timestamp (convert using MessagePack) in the format: -1, 0, type, timestamp
            WsSendBinary(MessagePackSerializer.Serialize(new object[] {-1, 0, TypeStrIdxLookup["int"], timestamp}));
        }
        
        private void WsHandleReceiveTimestamp(long serverTimestamp, long clientTimestamp) {
            long rxTime = GetClientTimeUs();

            // Recalculate server/client offset based on round trip time
            long rtt = rxTime - clientTimestamp;
            _networkLatencyUs = rtt / 2L;
            long serverTimeAtRx = serverTimestamp + _networkLatencyUs;
            _serverTimeOffsetUs = serverTimeAtRx - rxTime;

            Console.WriteLine(
                "[NT4] New server time: " +
                (GetServerTimeUs() / 1000000.0) +
                "s with " +
                (_networkLatencyUs / 1000.0) +
                "ms latency"
            );
        }
        
        // General Utility

        private static long GetClientTimeUs()
        {
            long timestamp = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;
            return timestamp * 1000;
        }

        /// <summary>
        /// Returns the current server time in microseconds. 
        /// This is calculated by an offset from the client time, so it may not be completely accurate.
        /// </summary>
        /// <returns>The current server time, in microseconds</returns>
        public long? GetServerTimeUs() {
            if (_serverTimeOffsetUs == null) return null;
            return GetClientTimeUs() + _serverTimeOffsetUs;
        }

        private void HandleJsonMessage(Dictionary<string, object> msg)
        {
            if(!msg.ContainsKey("method") || !msg.ContainsKey("params")) return;
            if (!(msg["method"] is string method)) return;
            if (!(msg["params"] is JObject parameters)) return;
            Dictionary<string, object>? parametersDict = parameters.ToObject<Dictionary<string, object>>();
            if(parametersDict == null)
            {
                Console.WriteLine("[NT4] Failed to decode JSON parameters: " + parameters);
                return;
            }
            if (method == "announce")
            {
                Nt4Topic topic = new Nt4Topic(parametersDict);
                if (_serverTopics.ContainsKey(topic.Name))
                {
                    Console.WriteLine("[NT4] Received announcement for topic that already exists: " + topic.Name);
                    _serverTopics.Remove(topic.Name);
                }
                _serverTopics.Add(topic.Name, topic);
            }
            else if (method == "unannounce")
            {
                if (parametersDict["name"] is string name)
                {
                    if (!_serverTopics.ContainsKey(name))
                    {
                        Console.WriteLine("[NT4] Received unannounce for topic that does not exist: " + name);
                        return;
                    }
                    _serverTopics.Remove(name);
                }
            }
            else if (method == "properties")
            {
                if (!(parametersDict["name"] is string name)) return;
                if (!_serverTopics.ContainsKey(name))
                {
                    Console.WriteLine("[NT4] Received properties update for topic that does not exist: " + name);
                    return;
                }
                Nt4Topic topic = _serverTopics[name];
                foreach (KeyValuePair<string, object> entry in (Dictionary<string, object>)parametersDict["update"])
                {
                    if (entry.Value == null)
                    {
                        topic.RemoveProperty(entry.Key);
                    }
                    else
                    {
                        topic.SetProperty(entry.Key, entry.Value);
                    }
                }
            }
        }

        private void HandleMsgPackMessage(object[] msg)
        {
            int topicId = Convert.ToInt32(msg[0]);
            long timestampUs = Convert.ToInt64(msg[1]);
            object value = msg[3];
            
            if (topicId >= 0)
            {
                Nt4Topic? topic = null;

                foreach (Nt4Topic serverTopic in _serverTopics.Values)
                {
                    if (serverTopic.Uid == topicId)
                    {
                        topic = serverTopic;
                        break;
                    }
                }
                if (topic == null) return;

                _onNewTopicData?.Invoke(topic, timestampUs, value);
            } else if (topicId == -1)
            {
                WsHandleReceiveTimestamp(timestampUs, Convert.ToInt64(value));
            }
        }

        private static int GetNewUid()
        {   
            return new Random().Next(0, 10000000);
        }

        public bool Connected()
        {
            return _ws != null && _ws.ReadyState == WebSocketState.Open;
        }
    }
}