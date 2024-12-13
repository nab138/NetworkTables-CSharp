using System.Collections.Generic;

namespace NetworkTablesSharp
{
    public class Nt4Subscription
    {

        public int Uid;
        private string[] _topics;
        private Nt4SubscriptionOptions _options;
        public Nt4Subscription(int uid, string[] topics, Nt4SubscriptionOptions options)
        {
            Uid = uid;
            _topics = topics;
            _options = options;
        }

        /**
         * Return a JSON object that can be sent to the server to subscribe to this subscription.
         */
        public Dictionary<string, object> ToSubscribeObj()
        {
            return new Dictionary<string, object>
            {
                {"topics", _topics},
                {"subuid", Uid},
                {"options", _options.ToObj()}
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