using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkTablesSharp
{
    /// <summary>
    ///     A user-friendly wrapper around the NetworkTables client for NT4 that keeps a local timestamped record of the values of topics.
    /// </summary>
    public class Nt4Source
    {
        public readonly Nt4Client Client;

        private readonly Dictionary<string, object> _values = [];

        private readonly Dictionary<string, (string, Dictionary<string, object>)> _queuedPublishes = [];
        private readonly Dictionary<string, Nt4SubscriptionOptions> _queuedSubscribes = [];

        /// <summary>
        /// Create a new NT4Source which automatically creates a client and connects to the server.
        /// </summary>
        /// <param name="serverAddress">The IP address of the server to connect to (default 127.0.0.1)</param>
        /// <param name="appName">The name of the application to connect as (default "Nt4Unity")</param>
        /// <param name="connectAutomatically">Whether to connect to the server when the object is created (default true)</param>
        /// <param name="port">The port to connect to the server on (default 5810)</param>
        public Nt4Source(string serverAddress = "127.0.0.1", string appName = "Nt4Unity", bool connectAutomatically = true, int port = 5810)
        {
            Client = new Nt4Client(appName, serverAddress, port, OnOpen, OnNewTopicData);
            if(connectAutomatically) Client.Connect();
        }

        /// <summary>
        /// Publish a topic to the server.
        /// </summary>
        /// <param name="topic">The topic to publish</param>
        /// <param name="type">The type of topic</param>
        public void PublishTopic(string topic, string type)
        {
            PublishTopic(topic, type, []);
        }

        /// <summary>
        /// Publish a topic to the server.
        /// </summary>
        /// <param name="">The topic to publish</param>
        /// <param name="type">The type of topic</param>
        /// <param name="properties">Properties of the topic</param>
        public void PublishTopic(string topic, string type, Dictionary<string, object> properties)
        {
            if (Client.Connected())
            {
                Client.PublishTopic(topic, type, properties);
            }
            _queuedPublishes.TryAdd(topic, (type, properties));
        }

        /// <summary>
        /// Publish a value to a topic
        /// </summary>
        /// <param name="topic">The topic key to publish to</param>
        /// <param name="value">The value to publish</param>
        public void PublishValue(string topic, object value)
        {
            if (Client.Connected())
            {
                Client.PublishValue(topic, value);
            }
        }

        /// <summary>
        /// Subscribe to a topic
        /// </summary>
        /// <param name="topic">The topic key to subscribe to</param>
        /// <param name="period">How frequently the server should send changes (in seconds).If unspecified, defaults to 0.1s</param>
        /// <param name="all">If true, the server should send all value changes over the wire. If false, only the most recent value is sent. If not specified, defaults to false.</param>
        /// <param name="topicsOnly">If true, the server should not send any value changes over the wire regardless of other options. This is useful for only getting topic announcements. If false, value changes are sent in accordance with other options. If not specified, defaults to false.</param>
        /// <param name="prefix">If true, any topic starting with the name in the subscription topics list is subscribed to, not just exact matches. If not specified, defaults to false.</param>
        public void Subscribe(string topic, double period = 0.1, bool all = false, bool topicsOnly = false, bool prefix = false)
        {
            if (Client.Connected())
            {
                Client.Subscribe(topic, period, all, topicsOnly, prefix);
            }
            _queuedSubscribes.TryAdd(topic, new Nt4SubscriptionOptions(period, all, topicsOnly, prefix));
        }

        /// <summary>
        /// Get the latest value of a topic
        /// </summary>
        /// <param name="key">The topic to get the value of</param>
        /// <returns>The latest value of the topic, or default if it doesn't exist</returns>
        public T? GetValue<T>(string key)
        {
            if (_values.TryGetValue(key, out var value))
            {
                return ((TopicValue<T>)value).GetValue();
            }

            return default;
        }

        /// <summary>
        /// Get the value of a topic at a specific timestamp
        /// </summary>
        /// <param name="key">The topic to get the value of</param>
        /// <param name="timestamp">The timestamp to get the value at</param>
        /// <returns>The most recent value before or at the given timestamp, or default if it doesn't exist</returns>
        public T? GetValue<T>(string key, long timestamp)
        {
            if (_values.TryGetValue(key, out var value))
            {
                return ((TopicValue<T>)value).GetValue(timestamp);
            }

            return default;
        }

        /// <summary>
        /// Check if the client is connected to the server
        /// </summary>
        /// <returns>True if the client is connected to the server</returns>
        public bool Connected()
        {
            return Client.Connected();
        }

        /// <summary>
        /// Connects the client if not already connected.
        /// </summary>
        public void Connect()
        {
            if(!Client.Connected()) Client.Connect();
        }

        /// <summary>
        /// Disconnects the client if connected.
        /// </summary>
        public void Disconnect()
        {
            if(Client.Connected()) Client.Disconnect();
        }

        /// <summary>
        /// Returns the current server time in microseconds. 
        /// This is calculated by an offset from the client time, so it may not be completely accurate.
        /// </summary>
        /// <returns>The current server time, in microseconds</returns>
        public long? GetServerTimeUs()
        {
            return Client.GetServerTimeUs();
        }

        private void OnOpen(object? sender, EventArgs e)
        {
            foreach (string topic in _queuedPublishes.Keys)
            {
                (string type, Dictionary<string, object> properties) = _queuedPublishes[topic];
                Client.PublishTopic(topic, type, properties);
            }
            foreach (string topic in _queuedSubscribes.Keys)
            {
                Client.Subscribe(topic, _queuedSubscribes[topic]);
            }
        }
        private void OnNewTopicData(Nt4Topic topic, long timestamp, object value)
        {
            switch (topic.Type)
            {
                case "string":
                    AddValue(topic.Name, timestamp, value.ToString());
                    break;
                case "boolean":
                    AddValue(topic.Name, timestamp, Convert.ToBoolean(value));
                    break;
                case "int":
                    AddValue(topic.Name, timestamp, Convert.ToInt64(value));
                    break;
                case "double":
                    AddValue(topic.Name, timestamp, Convert.ToDouble(value));
                    break;
                case "string[]":
                    AddValue(topic.Name, timestamp, ((object[])value).Cast<string>().ToArray());
                    break;
                case "boolean[]":
                    AddValue(topic.Name, timestamp, ((object[])value).Cast<bool>().ToArray());
                    break;
                case "int[]":
                    AddValue(topic.Name, timestamp, ((object[])value).Cast<long>().ToArray());
                    break;
                case "double[]":
                    AddValue(topic.Name, timestamp, ((object[])value).Cast<double>().ToArray());
                    break;
                default:
                    throw new ArgumentException($"Unknown type {topic.Type}");
            }
        }


        private void AddValue<T>(string key, long timestamp, T value)
        {
            if (!_values.ContainsKey(key))
            {
                _values[key] = new TopicValue<T>();
            }

            var topicValue = (TopicValue<T>)_values[key];
            topicValue.AddValue(timestamp, value);
        }
    }
}