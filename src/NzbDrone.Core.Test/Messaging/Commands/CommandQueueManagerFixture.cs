using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Messaging.Commands
{
    [TestFixture]
    public class CommandQueueManagerFixture : CoreTest<CommandQueueManager>
    {
        [SetUp]
        public void Setup()
        {
            var id = 0;
            var commands = new List<CommandModel>();

            Mocker.GetMock<ICommandRepository>()
                  .Setup(s => s.Insert(It.IsAny<CommandModel>()))
                  .Returns<CommandModel>(c =>
                  {
                      c.Id = id + 1;
                      commands.Add(c);
                      id++;

                      return c;
                  });

            Mocker.GetMock<ICommandRepository>()
                  .Setup(s => s.Get(It.IsAny<int>()))
                  .Returns<int>(c =>
                  {
                      return commands.SingleOrDefault(e => e.Id == c);
                  });
        }

        [Test]
        public void should_not_remove_commands_for_five_minutes_after_they_end()
        {
            var command = Subject.Push<RefreshMonitoredDownloadsCommand>(new RefreshMonitoredDownloadsCommand());

            // Start the command to mimic CommandQueue's behaviour
            command.StartedAt = DateTime.Now;
            command.Status = CommandStatus.Started;

            Subject.Start(command);
            Subject.Complete(command, "All done");
            Subject.CleanCommands();

            Subject.Get(command.Id).Should().NotBeNull();

            Mocker.GetMock<ICommandRepository>()
                  .Verify(v => v.Get(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_wait_to_persist_a_command_until_an_exclusive_command_completes()
        {
            var exclusive = Subject.Push(new ExclusiveCommand());
            using var consumer = Subject.Queue(CancellationToken.None).GetEnumerator();
            consumer.MoveNext().Should().BeTrue();

            using var started = new ManualResetEventSlim();
            var pendingPush = Task.Run(() =>
            {
                started.Set();
                return Subject.Push(new RefreshMonitoredDownloadsCommand());
            });

            try
            {
                started.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
                pendingPush.Wait(TimeSpan.FromMilliseconds(100)).Should().BeFalse();
            }
            finally
            {
                Subject.Complete(exclusive, "Done");
            }

            pendingPush.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
        }

        [Test]
        public void should_wait_to_persist_many_commands_until_an_exclusive_command_completes()
        {
            var exclusive = Subject.Push(new ExclusiveCommand());
            using var consumer = Subject.Queue(CancellationToken.None).GetEnumerator();
            consumer.MoveNext().Should().BeTrue();

            using var started = new ManualResetEventSlim();
            var pendingPush = Task.Run(() =>
            {
                started.Set();
                return Subject.PushMany(new List<RefreshMonitoredDownloadsCommand>
                {
                    new RefreshMonitoredDownloadsCommand()
                });
            });

            try
            {
                started.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
                pendingPush.Wait(TimeSpan.FromMilliseconds(100)).Should().BeFalse();
            }
            finally
            {
                Subject.Complete(exclusive, "Done");
            }

            pendingPush.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
        }

        private class ExclusiveCommand : Command
        {
            public override bool IsExclusive => true;
        }
    }
}
