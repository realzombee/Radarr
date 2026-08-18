using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListSyncCommandFixture
    {
        [Test]
        public void should_be_type_exclusive_without_globally_blocking_commands()
        {
            var command = new ImportListSyncCommand();

            command.IsExclusive.Should().BeFalse();
            command.IsTypeExclusive.Should().BeTrue();
        }
    }
}
