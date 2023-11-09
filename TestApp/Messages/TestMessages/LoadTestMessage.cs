using CommunityToolkit.Mvvm.Messaging.Messages;
using TestApp.Models;

namespace TestApp.Messages.TestMessages;

public sealed class LoadTestMessage : ValueChangedMessage<(TestModel, string)>
{
    public LoadTestMessage((TestModel, string) value) : base(value)
    {
    }
}