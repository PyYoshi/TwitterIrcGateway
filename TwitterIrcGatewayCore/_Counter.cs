using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Misuzilla.Applications.TwitterIrcGateway
{
    public static class _Counter
    {
        public static Int64 Status;
        public static Int64 Statuses;
        public static Int64 DirectMessage;
        public static Int64 DirectMessages;
        public static Int64 User;
        public static Int64 Users;
        public static Int64 Group;
        public static Int64 Groups;
        public static Int64 Filters;
        public static Int64 FilterItem;
        public static Int64 TwitterService;
        public static Int64 Session;
        public static Int64 Connection;

        public static void Increment(ref Int64 v)
        {
            Interlocked.Increment(ref v);
        }
        public static void Decrement(ref Int64 v)
        {
            Interlocked.Decrement(ref v);
        }
    }
}
