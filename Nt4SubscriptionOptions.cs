namespace NetworkTablesSharp
{
    public class Nt4SubscriptionOptions(double periodic = 0.1, bool all = false, bool topicsOnly = false, bool prefix = false)
    {
        public Dictionary<string, object> ToObj()
        {
            return new Dictionary<string, object>
            {
                { "periodic", periodic },
                { "all", all },
                { "topicsonly", topicsOnly },
                { "prefix", prefix }
            };
        }
    }
}