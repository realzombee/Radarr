using System;
using System.Runtime.CompilerServices;

namespace NzbDrone.Core.Datastore
{
    internal static class SqliteWriteCoordinator
    {
        private static readonly ConditionalWeakTable<IDatabase, object> Locks = new ConditionalWeakTable<IDatabase, object>();

        public static void Execute(IDatabase database, Action action)
        {
            if (database.DatabaseType != DatabaseType.SQLite)
            {
                action();
                return;
            }

            lock (Locks.GetValue(database, _ => new object()))
            {
                action();
            }
        }

        public static T Execute<T>(IDatabase database, Func<T> action)
        {
            if (database.DatabaseType != DatabaseType.SQLite)
            {
                return action();
            }

            lock (Locks.GetValue(database, _ => new object()))
            {
                return action();
            }
        }
    }
}
