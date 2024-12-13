using System.Collections.Generic;

namespace NetworkTablesSharp
{
    public class Nt4Subscription(int uid, string[] topics, Nt4SubscriptionOptions options)
    {
        public int Uid = uid;

        /**
         * Return a JSON object that can be sent to the server to subscribe to this subscription.
         */
        public Dictionary<string, object> ToSubscribeObj()
        {
            return new Dictionary<string, object>
            {
                {"topics", topics},
                {"subuid", Uid},
                {"options", options.ToObj()}
            };
        }

        public Dictionary<string, object> ToUnsubscribeObj()
        {
            return new Dictionary<string, object>
            {
                {"subuid", Uid}
            };
        }
    }
}