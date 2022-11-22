using System.Linq;
using TestApp.ViewModel.AppViewModel.Interfaces;

namespace TestApp.ViewModel.AppViewModel.Structure;

public readonly struct EditableAsk : IEditableAsk
{
    public string Question { get; init; }

    public bool IsSingleAnswers { get; init; }

    public (string Option, bool IsAnswered)[] Answers { get; init; }

    public EditableAsk(IAskViewModel ask)
    {
        Question = ask.Question;
        IsSingleAnswers = ask.IsSingleAnswers;
        Answers = ask.Answers.Select(answer => (answer.Option, answer.IsAnswered)).ToArray();
    }
}