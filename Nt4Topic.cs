using System.Collections.Generic;
using System;
using Newtonsoft.Json;

namespace NetworkTablesSharp
{
    public class Nt4Topic
    {
        public readonly int Uid;
        public readonly string Name;
        public readonly string Type;
        private readonly Dictionary<string, object> _properties;
        
        public Nt4Topic(int uid, string name, string type, Dictionary<string, object> properties)
        {
            Uid = uid;
            Name = name;
            Type = type;
            _properties = properties;
        }
        
        public Nt4Topic(Dictionary<string, object> obj)
        {
            Uid = Convert.ToInt32(obj["id"]);
            string? name = Convert.ToString(obj["name"]);
            string? type = Convert.ToString(obj["type"]);
            string? propertiesString = Convert.ToString(obj["properties"]);
            if (propertiesString == null || name == null || type == null)
            {
                throw new ArgumentException("Failed to deserialize topic object");
            }
            Dictionary<string, object>? properties = JsonConvert.DeserializeObject<Dictionary<string, object>>(propertiesString) ?? throw new ArgumentException("Failed to deserialize topic parameters object");

            Name = name;
            Type = type;
            _properties = properties;
        }

        public Dictionary<string, object> ToPublishObj()
        {
            return new Dictionary<string, object>
            {
                { "name", Name },
                { "type", Type },
                { "pubuid", Uid },
                { "properties", _properties }
            };
        }

        public Dictionary<string, object> ToUnpublishObj()
        {
            return new Dictionary<string, object>
            {
                { "pubuid", Uid }
            };
        }

        public int GetTypeIdx()
        {
            if (Nt4Client.TypeStrIdxLookup.ContainsKey(Type))
            {
                return Nt4Client.TypeStrIdxLookup[Type];
            }
        
            return 5; // Default to binary
        }

        public void SetProperty(string key, object value)
        {
            _properties[key] = value;
        }

        public void RemoveProperty(string key)
        {
            _properties.Remove(key);
        }
    }
}