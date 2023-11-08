using CommunityToolkit.Mvvm.Messaging.Messages;
using TestApp.ViewModels.Answer;

namespace TestApp.Messages.AnswerMessages;

public sealed class CreateAnswerMessage : ValueChangedMessage<IAnswerViewModel?>
{
    public CreateAnswerMessage(IAnswerViewModel? value) : base(value)
    {
    }
}