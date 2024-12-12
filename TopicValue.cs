using System.Collections.Generic;

namespace NetworkTables
{
    public class TopicValue<T>
    {
        private Dictionary<long, T> _timestampedValues = new Dictionary<long, T>();
        private T _latestValue = default(T);
        
        public void AddValue(long timestamp, T value)
        {
            _timestampedValues.TryAdd(timestamp, value);
            _latestValue = value;
        }
        
        public T GetValue()
        {
            return _latestValue;
        }
        
        public T GetValue(long timestamp)
        {
            if (_timestampedValues.ContainsKey(timestamp))
            {
                return _timestampedValues[timestamp];
            } else {
                // Find the most recent value before the given timestamp
                long closestTimestamp = long.MinValue;
                foreach (var ts in _timestampedValues.Keys)
                {
                    if (ts <= timestamp && ts > closestTimestamp)
                    {
                        closestTimestamp = ts;
                    }
                }
                if (closestTimestamp != long.MinValue)
                {
                    return _timestampedValues[closestTimestamp];
                }
            }

            return default(T);
        }
    }
}