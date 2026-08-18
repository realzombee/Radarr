using System.Data.SQLite;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class ConnectionStringFactoryFixture : DbTest
    {
        [Test]
        public void should_configure_sqlite_busy_timeout_for_one_second()
        {
            var connection = Mocker.Resolve<IConnectionStringFactory>().MainDbConnection.ConnectionString;
            var builder = new SQLiteConnectionStringBuilder(connection);

            builder.BusyTimeout.Should().Be(5000);
        }
    }
}
